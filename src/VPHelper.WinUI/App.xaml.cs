using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace VPHelper.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();
        // Capture any unhandled managed exception to a crash log. WinUI otherwise surfaces
        // startup failures only as an opaque stowed exception (0xc000027b) in Microsoft.ui.xaml.dll,
        // with no managed stack — this preserves the real cause for diagnosis.
        this.UnhandledException += (_, e) =>
        {
            LogCrash("App.UnhandledException", e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };
    }

    /// <summary>Append a fatal exception (message, type, full stack, inner chain) to crash.log.</summary>
    internal static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var path = Path.Combine(AppHost.DataFolder, "crash.log");
            Directory.CreateDirectory(AppHost.DataFolder);
            File.AppendAllText(path,
                $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* logging must never mask the original failure */ }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            AppHost.Initialize();
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            LogCrash("OnLaunched", ex);
            throw;
        }
    }
}
