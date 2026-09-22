using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VPHelper.Core.Models;
using VPHelper.WinUI.Pages;
using VPHelper.WinUI.Services;
using Windows.Graphics;

namespace VPHelper.WinUI;

public sealed partial class MainWindow : Window
{
    // ---- nav pane resize -------------------------------------------------------
    private const double NavPaneMinWidth = 176;
    private const double NavPaneMaxWidth = 360;
    private const double NavPaneDefaultWidth = 220;
    private const int MinimumSavedWindowWidth = 480;
    private const int MinimumSavedWindowHeight = 360;

    private double _navPaneWidth = NavPaneDefaultWidth;
    private bool _resizing;
    private double _resizeStartX;
    private double _resizeStartWidth;
    private RectInt32? _normalWindowBounds;
    private bool _restoreWindowMaximized;
    private bool _restoringWindowPlacement;
    private readonly DispatcherQueueTimer _settingsSaveTimer;
    private readonly WindowSizeGuard _windowSizeGuard;

    /// <summary>Current nav pane open width (bound to <c>OpenPaneLength</c> in XAML).</summary>
    public double NavPaneWidth
    {
        get => _navPaneWidth;
        private set
        {
            _navPaneWidth = Math.Clamp(value, NavPaneMinWidth, NavPaneMaxWidth);
            // x:Bind OneWay reads the property via NotifyPropertyChanged-like mechanism but
            // MainWindow uses simple x:Bind not MVVM — force a re-evaluate by updating the
            // Margin binding source. Using a binding to a CLR property without INotifyPropertyChanged
            // means we must call UpdateBindings explicitly in WinUI 3.
            Bindings?.Update();
        }
    }

    /// <summary>Left margin used to position the drag handle at the pane's right edge.</summary>
    public Thickness PaneResizeMargin =>
        new Thickness(NavPaneWidth - 3, 0, 0, 0);

    // ---- settings persistence --------------------------------------------------
    private static string SettingsPath =>
        Path.Combine(AppHost.DataFolder, "ui_settings.json");

    private void LoadUiSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;

            var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsPath));
            if (settings == null) return;

            if (settings.NavPaneWidth is double navPaneWidth)
                _navPaneWidth = Math.Clamp(navPaneWidth, NavPaneMinWidth, NavPaneMaxWidth);

            if (settings.WindowX is int x && settings.WindowY is int y &&
                settings.WindowWidth is int width && settings.WindowHeight is int height &&
                width >= MinimumSavedWindowWidth && height >= MinimumSavedWindowHeight)
            {
                _normalWindowBounds = new RectInt32(x, y, width, height);
            }

            _restoreWindowMaximized = settings.WindowMaximized;
        }
        catch { /* use defaults */ }
    }

    private void SaveUiSettings()
    {
        try
        {
            CaptureNormalWindowBounds();
            var bounds = _normalWindowBounds;
            var json = JsonSerializer.Serialize(new UiSettings
            {
                NavPaneWidth = _navPaneWidth,
                WindowX = bounds?.X,
                WindowY = bounds?.Y,
                WindowWidth = bounds?.Width,
                WindowHeight = bounds?.Height,
                WindowMaximized = _restoreWindowMaximized,
            });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    // ---- constructor -----------------------------------------------------------

    public MainWindow()
    {
        LoadUiSettings();
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        if (AppWindowTitleBar.IsCustomizationSupported())
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetTitleBar(AppTitleBar);
        Title = "VP Helper";

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        _windowSizeGuard = new WindowSizeGuard(this);
        RestoreWindowPlacement();
        _settingsSaveTimer = DispatcherQueue.CreateTimer();
        _settingsSaveTimer.Interval = TimeSpan.FromMilliseconds(350);
        _settingsSaveTimer.IsRepeating = false;
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            SaveUiSettings();
        };
        AppWindow.Changed += OnAppWindowChanged;
        Closed += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            SaveUiSettings();
            AppWindow.Changed -= OnAppWindowChanged;
            _windowSizeGuard.Dispose();
        };

        AttributionText.Text = AppHost.Get<ViewModels.MainViewModel>().DataAttribution;

        // After sync completes, refresh the dashboard silently but stay on the current page.
        AppHost.Get<ViewModels.LoginViewModel>().SyncCompleted += () =>
            DispatcherQueue.TryEnqueue(async () =>
                await AppHost.Get<ViewModels.DashboardViewModel>().RefreshAsync());
    }

    private void RestoreWindowPlacement()
    {
        _restoringWindowPlacement = true;
        try
        {
            var work = _windowSizeGuard.WorkArea(_normalWindowBounds);
            var saved = _normalWindowBounds is { } old ? new WindowBounds(old.X, old.Y, old.Width, old.Height) : (WindowBounds?)null;
            var fitted = WindowLayoutPolicy.Restore(saved, new WindowBounds(work.X, work.Y, work.Width, work.Height), _windowSizeGuard.Scale);
            var restoredBounds = new RectInt32(fitted.X, fitted.Y, fitted.Width, fitted.Height);
            AppWindow.MoveAndResize(restoredBounds);
            _normalWindowBounds = restoredBounds;

            if (_restoreWindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();
        }
        finally
        {
            _restoringWindowPlacement = false;
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_restoringWindowPlacement ||
            (!args.DidSizeChange && !args.DidPositionChange && !args.DidPresenterChange))
            return;

        CaptureNormalWindowBounds();
        if (AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Minimized } presenter)
            _restoreWindowMaximized = presenter.State == OverlappedPresenterState.Maximized;
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void CaptureNormalWindowBounds()
    {
        if (AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
            return;

        var size = AppWindow.Size;
        if (size.Width < MinimumSavedWindowWidth || size.Height < MinimumSavedWindowHeight)
            return;

        var position = AppWindow.Position;
        _normalWindowBounds = new RectInt32(position.X, position.Y, size.Width, size.Height);
    }

    private sealed class UiSettings
    {
        [JsonPropertyName("navPaneWidth")]
        public double? NavPaneWidth { get; init; }

        [JsonPropertyName("windowX")]
        public int? WindowX { get; init; }

        [JsonPropertyName("windowY")]
        public int? WindowY { get; init; }

        [JsonPropertyName("windowWidth")]
        public int? WindowWidth { get; init; }

        [JsonPropertyName("windowHeight")]
        public int? WindowHeight { get; init; }

        [JsonPropertyName("windowMaximized")]
        public bool WindowMaximized { get; init; }
    }

    // ---- nav -------------------------------------------------------------------

    private void Nav_Loaded(object sender, RoutedEventArgs e)
    {
        Nav.SelectedItem = Nav.MenuItems[0];
        UpdatePaneResizeVisibility();
    }

    private void OnNavDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        => UpdatePaneResizeVisibility();

    private void UpdatePaneResizeVisibility()
    {
        if (PaneResizeHandle != null)
            PaneResizeHandle.Visibility = Nav.DisplayMode == NavigationViewDisplayMode.Expanded
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        Navigate(item.Tag as string);
    }

    private ContestNavigationRequest? _pendingContestNavigation;

    private void Navigate(string? tag)
    {
        switch (tag)
        {
            case "dashboard":
                var dashPage = new DashboardPage();
                dashPage.NavigateRequested += (_, t) => SelectNav(t);
                dashPage.ContestRequested += (_, request) => SelectContest(request.Platform, request.GymId);
                ContentFrame.Content = dashPage;
                break;
            case "contests":
                var contestsPage = new ContestsPage();
                ContentFrame.Content = contestsPage;
                if (_pendingContestNavigation != null)
                {
                    var request = _pendingContestNavigation;
                    _pendingContestNavigation = null;
                    _ = contestsPage.SelectContestAsync(request.Platform, request.GymId, request.Handle);
                }
                break;
            case "official":      ContentFrame.Content = new OfficialPage(); break;
            case "gymdirectory":
                var directoryPage = new GymDirectoryPage();
                directoryPage.ContestRequested += (_, request) => SelectContest(request.Platform, request.GymId, request.Handle);
                ContentFrame.Content = directoryPage;
                break;
            case "login":        ContentFrame.Content = new LoginPage(); break;
        }
    }

    internal void SelectNav(string tag)
    {
        foreach (var mi in Nav.MenuItems)
            if (mi is NavigationViewItem nvi && (nvi.Tag as string) == tag)
            { Nav.SelectedItem = nvi; return; }
        foreach (var mi in Nav.FooterMenuItems)
            if (mi is NavigationViewItem nvi && (nvi.Tag as string) == tag)
            { Nav.SelectedItem = nvi; return; }
    }

    private void SelectContest(Platform platform, long gymId, string? handle = null)
    {
        if (ContentFrame.Content is ContestsPage page)
        {
            _ = page.SelectContestAsync(platform, gymId, handle);
            return;
        }
        _pendingContestNavigation = new ContestNavigationRequest(platform, gymId, handle);
        SelectNav("contests");
    }

    // ---- pane resize pointer events --------------------------------------------

    private void OnResizePointerEntered(object sender, PointerRoutedEventArgs e)
        => ResizeIndicator.Opacity = 0.6;

    private void OnResizePointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) ResizeIndicator.Opacity = 0;
    }

    private void OnResizePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _resizing = true;
        _resizeStartX = e.GetCurrentPoint(RootGrid).Position.X;
        _resizeStartWidth = NavPaneWidth;
        PaneResizeHandle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnResizePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        double currentX = e.GetCurrentPoint(RootGrid).Position.X;
        double delta = currentX - _resizeStartX;
        NavPaneWidth = _resizeStartWidth + delta;
        e.Handled = true;
    }

    private void OnResizePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        PaneResizeHandle.ReleasePointerCaptures();
        ResizeIndicator.Opacity = 0;
        SaveUiSettings();
        e.Handled = true;
    }
}
