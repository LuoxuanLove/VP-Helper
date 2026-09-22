using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VPHelper.Core.Data;
using VPHelper.Core.Rankland;
using VPHelper.Core.Scraping;
using VPHelper.Core.Services;
using VPHelper.WinUI.Services;
using VPHelper.WinUI.ViewModels;

namespace VPHelper.WinUI;

/// <summary>Composition root: wires Core services, the SQLite store, and view models.</summary>
public static class AppHost
{
    public static IServiceProvider Services { get; private set; } = default!;

    /// <summary>
    /// Data folder relative to the executable directory — easy to find, backup, and move with the
    /// installation. Falls back to %LOCALAPPDATA%\VPHelper if the exe dir is not writable (rare in
    /// dev scenarios but can happen under MSIX packaging).
    /// </summary>
    public static string DataFolder { get; } = ResolveDataFolder();

    private static string ResolveDataFolder()
    {
        // Prefer ./data/ next to the executable so the whole installation is self-contained.
        try
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(candidate);
            // Verify writability with a probe file.
            var probe = Path.Combine(candidate, ".write_probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return candidate;
        }
        catch
        {
            // Packaged/restricted environment: fall back to user's local app data.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VPHelper");
        }
    }

    /// <summary>Resolve a service from the root provider.</summary>
    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public static void Initialize()
    {
        Directory.CreateDirectory(DataFolder);
        var services = new ServiceCollection();

        var dbPath = Path.Combine(DataFolder, "vphelper.db");
        // A pooled context factory: EF Core DbContext is not thread-safe, so each unit of work
        // gets its own short-lived context rather than sharing a singleton across async VMs.
        services.AddDbContextFactory<VpDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

        // Rankland uses plain HTTPS (raw.githubusercontent is not behind Cloudflare).
        services.AddSingleton<IRawContentSource>(_ => new HttpRawContentSource());
        services.AddSingleton(sp => new RanklandClient(sp.GetRequiredService<IRawContentSource>()));

        // The browser fetchers are set later once each platform's WebView2 is ready.
        services.AddSingleton<BrowserFetcherHolder>();

        // View models (each owns a context factory and creates per-operation contexts).
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<ContestsViewModel>();
        services.AddSingleton<OfficialViewModel>();
        services.AddSingleton<GymDirectoryViewModel>();

        Services = services.BuildServiceProvider();

        // Ensure schema exists, then reconcile any columns added after this DB file was first
        // created (EnsureCreated never alters an existing schema — see SchemaGuard).
        var factory = Services.GetRequiredService<IDbContextFactory<VpDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        SchemaGuard.Reconcile(db);
        DerivedDataGuard.Reconcile(db);
        CuratedMappingGuard.Reconcile(db);
    }
}

/// <summary>
/// Holds the live <see cref="IBrowserFetcher"/> instances for each platform once their WebView2
/// controls are initialized. CF and QOJ require separate WebView2 sessions because cookies are
/// origin-scoped: a QOJ fetch made from the CF WebView2 context would lack QOJ session cookies
/// and always receive 302/401 responses.
/// </summary>
public sealed class BrowserFetcherHolder
{
    /// <summary>Codeforces fetcher — set when the CF WebView2 on the login page is ready.</summary>
    public IBrowserFetcher? CfFetcher { get; set; }

    /// <summary>QOJ fetcher — set when the QOJ WebView2 on the login page is ready.</summary>
    public IBrowserFetcher? QojFetcher { get; set; }

    /// <summary>Backward-compat shim used by legacy callers that only know about one fetcher.</summary>
    public IBrowserFetcher? Fetcher
    {
        get => CfFetcher;
        set => CfFetcher = value;
    }

    /// <summary>Minimum interval between fetches (throttle), configurable via settings later.</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromSeconds(2);

    public bool IsCfReady => CfFetcher != null;
    public bool IsQojReady => QojFetcher != null;
    /// <summary>True when at least one fetcher is ready (any platform can sync).</summary>
    public bool IsReady => IsCfReady || IsQojReady;

    /// <summary>
    /// Build the Codeforces HTML scraper + participant-type API client over a shared throttle.
    /// </summary>
    public (SubmissionScraper Scraper, ContestStatusApi Api, GymCloneService CloneService) CreateCfSyncPipeline()
    {
        if (CfFetcher == null) throw new InvalidOperationException("Codeforces login required before scraping.");
        var throttle = new Throttle(MinInterval);
        var scraper = new SubmissionScraper(CfFetcher, throttle, new BackoffPolicy());
        var api = new ContestStatusApi(CfFetcher, throttle, new BackoffPolicy());
        var cloneService = new GymCloneService(CfFetcher, throttle, new BackoffPolicy());
        return (scraper, api, cloneService);
    }

    /// <summary>Backward-compat wrapper used by existing callers.</summary>
    public (SubmissionScraper Scraper, ContestStatusApi Api, GymCloneService CloneService) CreateSyncPipeline()
        => CreateCfSyncPipeline();

    /// <summary>Build a <see cref="QojSyncService"/> over the QOJ browser context.</summary>
    public QojSyncService CreateQojSyncPipeline(VPHelper.Core.Data.VpDbContext db)
    {
        if (QojFetcher == null) throw new InvalidOperationException("QOJ login required before scraping.");
        var throttle = new Throttle(MinInterval);
        return new QojSyncService(db, QojFetcher, throttle, new BackoffPolicy());
    }
}
