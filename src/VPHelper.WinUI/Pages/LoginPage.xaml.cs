using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VPHelper.Core.Models;
using VPHelper.WinUI.Services;
using VPHelper.WinUI.ViewModels;

namespace VPHelper.WinUI.Pages;

/// <summary>
/// Manages the two-platform (CF + QOJ) login page. A single WebView2 navigates between platforms
/// based on which platform tab is active. The same <see cref="WebView2BrowserFetcher"/> instance is
/// registered as both <c>CfFetcher</c> and <c>QojFetcher</c> in <see cref="BrowserFetcherHolder"/>
/// — because <c>fetch()</c> runs in the WebView's page context, the active page's origin always
/// provides the right session cookies for whichever platform is currently shown.
/// </summary>
public sealed partial class LoginPage : Page
{
    public LoginViewModel Vm { get; }
    private readonly BrowserFetcherHolder _holder;

    private bool _webReady;
    private bool _detecting;
    private bool _fittingBrowser;
    private Platform _activePlatform = Platform.Codeforces;

    // Periodic re-detection keeps the login banner honest when CF's Cloudflare clears in-page.
    private readonly DispatcherQueueTimer _detectTimer;

    public LoginPage()
    {
        InitializeComponent();
        Vm = AppHost.Get<LoginViewModel>();
        _holder = AppHost.Get<BrowserFetcherHolder>();
        _detectTimer = DispatcherQueue.CreateTimer();
        _detectTimer.Interval = TimeSpan.FromSeconds(2);
        _detectTimer.Tick += async (_, _) => await DetectAsync();
        Loaded += OnLoaded;
        Unloaded += (_, _) => _detectTimer.Stop();
    }

    // ---- lifecycle -------------------------------------------------------------

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Vm.RefreshAccountsAsync();

        if (_webReady)
        {
            await DetectAsync();
            _detectTimer.Start();
            return;
        }
        try
        {
            Web.DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);

            // Persist WebView2 profile for session survival (cookies / cf_clearance).
            var userData = Path.Combine(AppHost.DataFolder, "WebView2");
            Directory.CreateDirectory(userData);
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                .CreateWithOptionsAsync(null, userData, null);
            await Web.EnsureCoreWebView2Async(env);
            _webReady = true;
            Web.CoreWebView2.Settings.IsZoomControlEnabled = true;

            // One fetcher instance reused for both platforms (see class-level comment).
            var fetcher = new WebView2BrowserFetcher(Web.CoreWebView2, DispatcherQueue);
            _holder.CfFetcher = fetcher;
            _holder.QojFetcher = fetcher;

            Web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            Web.CoreWebView2.Navigate(_activePlatform == Platform.Codeforces
                ? "https://codeforces.com/"
                : "https://qoj.ac/");
            _detectTimer.Start();
        }
        catch (Exception ex)
        {
            Vm.CfStatus = $"无法初始化 WebView2：{ex.Message}";
        }
    }

    private async void OnNavigationCompleted(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess)
        {
            BrowserFitStatusLabel.Text = "正在适应";
            await Task.Delay(80);
            await FitBrowserWidthAsync();
        }
        else
        {
            BrowserFitStatusLabel.Text = "网页加载失败";
        }

        await DetectAsync();
    }

    // ---- platform tab switching ------------------------------------------------

    private void OnSelectCfPlatform(object sender, RoutedEventArgs e)
        => SelectPlatform(Platform.Codeforces, navigate: true);

    private void OnSelectQojPlatform(object sender, RoutedEventArgs e)
        => SelectPlatform(Platform.Qoj, navigate: true);

    private void SelectPlatform(Platform platform, bool navigate)
    {
        var changed = _activePlatform != platform;
        _activePlatform = platform;
        CfLoginStatePanel.Visibility = platform == Platform.Codeforces ? Visibility.Visible : Visibility.Collapsed;
        QojLoginStatePanel.Visibility = platform == Platform.Qoj ? Visibility.Visible : Visibility.Collapsed;
        CfTabIndicator.Visibility = platform == Platform.Codeforces ? Visibility.Visible : Visibility.Collapsed;
        QojTabIndicator.Visibility = platform == Platform.Qoj ? Visibility.Visible : Visibility.Collapsed;
        BrowserPlatformLabel.Text = _activePlatform == Platform.Codeforces
            ? "Codeforces 浏览器"
            : "QOJ 浏览器";
        if (!navigate || !_webReady || !changed)
            return;

        BrowserFitStatusLabel.Text = "正在加载";
        var url = _activePlatform == Platform.Codeforces
            ? "https://codeforces.com/"
            : "https://qoj.ac/";
        Web.CoreWebView2?.Navigate(url);
    }

    private async void OnFitBrowserWidth(object sender, RoutedEventArgs e)
        => await FitBrowserWidthAsync();

    private async Task FitBrowserWidthAsync()
    {
        if (!_webReady || _fittingBrowser || Web.CoreWebView2 == null)
            return;

        _fittingBrowser = true;
        try
        {
            const string script = @"(function() {
  try {
    var root = document.documentElement;
    var body = document.body;
    if (!root || !body) return JSON.stringify({ ok: false, scale: 1, overflow: 0 });

    root.style.zoom = '';
    var viewport = Math.max(1, root.clientWidth);
    var content = Math.max(viewport, root.scrollWidth, body.scrollWidth, body.offsetWidth);
    var scale = Math.min(1, (viewport - 12) / content);
    scale = Math.max(0.55, Math.floor(scale * 100) / 100);
    root.style.zoom = scale < 0.995 ? scale.toFixed(2) : '';
    if (scale >= 0.995) scale = 1;
    return JSON.stringify({ ok: true, scale: scale });
  } catch (e) {
    return JSON.stringify({ ok: false, scale: 1, overflow: 0 });
  }
})()";

            var raw = await Web.CoreWebView2.ExecuteScriptAsync(script);
            var inner = System.Text.Json.JsonSerializer.Deserialize<string>(raw);
            var result = string.IsNullOrWhiteSpace(inner)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<BrowserFitResult>(inner);
            BrowserFitStatusLabel.Text = result?.Ok == true
                ? $"已适应 · {result.Scale:P0}"
                : "适应失败";
        }
        catch
        {
            BrowserFitStatusLabel.Text = "适应失败";
        }
        finally
        {
            _fittingBrowser = false;
        }
    }

    // ---- "+" add account buttons -----------------------------------------------

    private void OnAddCfAccount(object sender, RoutedEventArgs e)
    {
        // Switch to CF tab and navigate to the login page.
        SelectPlatform(Platform.Codeforces, navigate: false);
        if (_webReady)
            Web.CoreWebView2?.Navigate("https://codeforces.com/enter");
    }

    private void OnAddQojAccount(object sender, RoutedEventArgs e)
    {
        // Switch to QOJ tab and navigate to the QOJ login page.
        SelectPlatform(Platform.Qoj, navigate: false);
        if (_webReady)
            Web.CoreWebView2?.Navigate("https://qoj.ac/login");
    }

    // ---- detection -------------------------------------------------------------

    private async Task DetectAsync()
    {
        if (!_webReady || _detecting || Vm.IsBusy) return;
        _detecting = true;
        try
        {
            if (_activePlatform == Platform.Codeforces)
                await DetectCfAsync();
            else
                await DetectQojAsync();
        }
        catch { /* best-effort */ }
        finally { _detecting = false; }
    }

    private async Task DetectCfAsync()
    {
        // Revised detection: find the logout link first, then walk backwards to the adjacent
        // profile link. On CF the nav-bar is "[Handle] | [Logout]", so the profile link is the
        // immediate DOM sibling/predecessor of the logout link. The old approach
        // (querySelector on '#header a[href^="/profile/"]') is ambiguous on blog pages where
        // the blog author's profile link appears inside #header before the nav-bar links.
        const string js = @"(function(){
  try {
    var html = document.documentElement ? document.documentElement.innerHTML : '';
    var title = document.title || '';
    var challenge = !!document.querySelector('#challenge-form,#challenge-running,script[src*=""challenge-platform""]')
                    || /just a moment|checking your browser/i.test(title + ' ' + html);
    if (challenge) return JSON.stringify({ state: 'cf', handle: '' });

    // Check logout link anywhere in the document (CF uses a CSRF-token path ending /logout).
    var logoutLink = null;
    var allLinks = document.querySelectorAll('a');
    for (var i = 0; i < allLinks.length; i++) {
      if (allLinks[i].textContent.trim() === 'Logout') { logoutLink = allLinks[i]; break; }
    }
    if (!logoutLink) logoutLink = document.querySelector('a[href*=""logout""]');
    if (!logoutLink) return JSON.stringify({ state: 'out', handle: '' });

    // Strategy 1: walk previous sibling nodes from the logout link looking for a profile link.
    var handle = '';
    var node = logoutLink.previousSibling;
    while (node) {
      if (node.nodeType === 1 && node.tagName === 'A') {
        var href = node.getAttribute('href') || '';
        var m = href.match(/^\/profile\/([^\/?#]+)/);
        if (m) { handle = m[1]; break; }
      }
      node = node.previousSibling;
    }

    // Strategy 2: look in the logout link's parent element (covers one-div-wraps-both cases).
    if (!handle && logoutLink.parentElement) {
      var links = logoutLink.parentElement.querySelectorAll('a[href^=""/profile/""]');
      if (links.length > 0) {
        var m2 = links[links.length - 1].getAttribute('href').match(/^\/profile\/([^\/?#]+)/);
        if (m2) handle = m2[1];
      }
    }

    // Strategy 3: walk up the DOM until we find a containing block with a profile link.
    if (!handle) {
      var parent = logoutLink.parentElement;
      var depth = 0;
      while (parent && depth < 5) {
        var links3 = parent.querySelectorAll('a[href^=""/profile/""]');
        if (links3.length > 0) {
          // Pick the last profile link in that container — in CF nav it is the logged-in user.
          var m3 = links3[links3.length - 1].getAttribute('href').match(/^\/profile\/([^\/?#]+)/);
          if (m3) { handle = m3[1]; break; }
        }
        parent = parent.parentElement;
        depth++;
      }
    }

    // Strategy 4: CF occasionally renders the current user link away from the logout link.
    // Prefer profile links whose visible text exactly equals their URL segment.
    if (!handle) {
      var candidates = document.querySelectorAll('a[href^=""/profile/""]');
      for (var j = candidates.length - 1; j >= 0; j--) {
        var href4 = candidates[j].getAttribute('href') || '';
        var m4 = href4.match(/^\/profile\/([^\/?#]+)/);
        if (!m4) continue;
        var text4 = (candidates[j].textContent || '').trim();
        if (text4 === m4[1]) { handle = m4[1]; break; }
      }
    }

    return JSON.stringify({ state: 'in', handle: handle });
  } catch(e) { return JSON.stringify({ state: 'err', handle: '' }); }
})()";
        var raw = await Web.CoreWebView2.ExecuteScriptAsync(js);
        var inner = System.Text.Json.JsonSerializer.Deserialize<string>(raw);
        if (string.IsNullOrEmpty(inner)) return;
        var det = System.Text.Json.JsonSerializer.Deserialize<Detection>(inner);
        if (det == null) return;
        Vm.ApplyCfDetection(det.State switch
        {
            "cf" => LoginState.CloudflareChallenge,
            "in" => LoginState.LoggedIn,
            "out" => LoginState.NotLoggedIn,
            _ => LoginState.Initializing,
        }, det.Handle);
    }

    private async Task DetectQojAsync()
    {
        // QOJ pages contain many profile links (blog authors, rankings, teams). Only a profile
        // link in the same top-bar container as Logout identifies the current session.
        const string js = @"(function(){
  try {
    var all = document.querySelectorAll('a');
    var logout = null;
    for (var i = 0; i < all.length; i++) {
      var href = all[i].getAttribute('href') || '';
      if (href.indexOf('/logout') >= 0 || (all[i].textContent || '').trim() === 'Logout') {
        logout = all[i];
        break;
      }
    }
    if (!logout) return JSON.stringify({ state: 'out', handle: '' });

    var handle = '';
    var container = logout.parentElement;
    var depth = 0;
    while (container && depth < 5 && !handle) {
      var links = container.querySelectorAll('a[href*=""/user/profile/""]');
      for (var j = 0; j < links.length; j++) {
        var m = (links[j].getAttribute('href') || '').match(/\/user\/profile\/([^\/?#]+)/);
        if (m && m[1] && m[1] !== 'undefined') { handle = decodeURIComponent(m[1]); break; }
      }
      container = container.parentElement;
      depth++;
    }
    return JSON.stringify({ state: 'in', handle: handle });
  } catch(e) { return JSON.stringify({ state: 'err', handle: '' }); }
})()";
        var raw = await Web.CoreWebView2.ExecuteScriptAsync(js);
        var inner = System.Text.Json.JsonSerializer.Deserialize<string>(raw);
        if (string.IsNullOrEmpty(inner)) return;
        var det = System.Text.Json.JsonSerializer.Deserialize<Detection>(inner);
        if (det == null) return;
        Vm.ApplyQojDetection(det.State switch
        {
            "in" => LoginState.LoggedIn,
            "out" => LoginState.NotLoggedIn,
            _ => LoginState.Initializing,
        }, det.Handle);
    }

    // ---- sync buttons ----------------------------------------------------------

    private void OnCancelSync(object sender, RoutedEventArgs e) => Vm.Cancel();

    private async void OnSyncCf(object sender, RoutedEventArgs e) => await Vm.SyncCfAsync();
    private async void OnSyncQoj(object sender, RoutedEventArgs e) => await Vm.SyncQojAsync();

    // ---- account management ----------------------------------------------------

    private async void OnRemoveAccount(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string handle }) return;
        // Determine platform from the AccountItem in the sender's DataContext.
        var platform = sender is FrameworkElement fe && fe.DataContext is AccountItem ai
            ? ai.Platform
            : Platform.Codeforces;
        await Vm.RemoveAccountAsync(platform, handle);
    }

    private async void OnResolveConflict(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ConflictCandidateItem cand })
            await Vm.ResolveConflictAsync(cand.Platform, cand.GymId, cand.Handle);
    }

    private sealed class Detection
    {
        [System.Text.Json.Serialization.JsonPropertyName("state")] public string? State { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("handle")] public string? Handle { get; set; }
    }

    private sealed class BrowserFitResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("ok")] public bool Ok { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("scale")] public double Scale { get; set; } = 1;
        [System.Text.Json.Serialization.JsonPropertyName("overflow")] public double Overflow { get; set; }
    }
}

