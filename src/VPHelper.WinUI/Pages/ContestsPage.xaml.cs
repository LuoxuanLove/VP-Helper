using System;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using VPHelper.WinUI.ViewModels;

namespace VPHelper.WinUI.Pages;

public sealed partial class ContestsPage : Page
{
    private const double MinContestListWidth = 250;
    private const double MinDetailWidth = 470;
    private const double MinStandingWidth = 290;
    private const double MaxContestListWidth = 560;
    private const double MaxStandingWidth = 680;

    public ContestsViewModel Vm { get; }
    private System.Threading.Tasks.Task? _initialLoad;
    private ResizeTarget _resizeTarget = ResizeTarget.None;
    private double _resizeStartX;
    private double _resizeStartLeftWidth;
    private double _resizeStartRightWidth;
    private bool _updatingProblemSortButtons;
    private bool _compactLayout;
    private bool _showCompactStandings;

    public ContestsPage()
    {
        InitializeComponent();
        Vm = AppHost.Get<ContestsViewModel>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => NormalizeColumnWidths();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Vm.OfficialStandingsLoaded -= OnOfficialStandingsLoaded;
        Vm.OfficialStandingsLoaded += OnOfficialStandingsLoaded;
        await EnsureLoadedAsync();
    }

    private System.Threading.Tasks.Task EnsureLoadedAsync() => _initialLoad ??= Vm.LoadContestsAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => Vm.OfficialStandingsLoaded -= OnOfficialStandingsLoaded;

    private void OnOfficialStandingsLoaded()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            var vpIndex = Vm.OfficialStandings
                .Select((row, index) => (row, index))
                .FirstOrDefault(item => item.row.IsVpRow)
                .index;
            if (vpIndex < 0 || vpIndex >= Vm.OfficialStandings.Count ||
                !Vm.OfficialStandings[vpIndex].IsVpRow)
                return;

            // Keep nearby official teams visible instead of pinning the VP row to the top.
            var contextAnchor = Vm.OfficialStandings[Math.Max(0, vpIndex - 5)];
            OfficialStandingsList.ScrollIntoView(contextAnchor, ScrollIntoViewAlignment.Leading);
        });
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
        => await Vm.LoadContestsAsync();

    private void OnContestSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            Vm.ContestSearchText = textBox.Text;
    }

    private void OnClearContestSearch(object sender, RoutedEventArgs e)
    {
        ContestSearchBox.Text = string.Empty;
        Vm.ContestSearchText = string.Empty;
        ContestSearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnContestRanklandClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    public async System.Threading.Tasks.Task SelectContestAsync(VPHelper.Core.Models.Platform platform, long gymId, string? handle = null)
    {
        await EnsureLoadedAsync();
        await Vm.SelectContestAsync(platform, gymId, handle);
    }

    private async void OnProblemLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnDifficultySortClick(object sender, RoutedEventArgs e)
        => SetProblemSortMode(ProblemSortMode.Difficulty);

    private void OnContestOrderSortClick(object sender, RoutedEventArgs e)
        => SetProblemSortMode(ProblemSortMode.ContestOrder);

    private void SetProblemSortMode(ProblemSortMode sortMode)
    {
        if (_updatingProblemSortButtons)
            return;

        _updatingProblemSortButtons = true;
        DifficultySortButton.IsChecked = sortMode == ProblemSortMode.Difficulty;
        ContestOrderSortButton.IsChecked = sortMode == ProblemSortMode.ContestOrder;
        _updatingProblemSortButtons = false;
        Vm.SetProblemSortMode(sortMode);
    }

    private void OnResizeHandlePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            ResizeIndicatorFor(element).Opacity = 0.6;
    }

    private void OnResizeHandlePointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeTarget != ResizeTarget.None || sender is not FrameworkElement element)
            return;

        ResizeIndicatorFor(element).Opacity = 0;
    }

    private void OnLeftResizePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        BeginResize(ResizeTarget.Left, sender, e);
    }

    private void OnRightResizePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        BeginResize(ResizeTarget.Right, sender, e);
    }

    private void OnResizePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeTarget == ResizeTarget.None)
            return;

        var currentX = e.GetCurrentPoint(this).Position.X;
        var delta = currentX - _resizeStartX;
        if (_resizeTarget == ResizeTarget.Left)
            SetContestListWidth(_resizeStartLeftWidth + delta);
        else
            SetStandingWidth(_resizeStartRightWidth - delta);

        e.Handled = true;
    }

    private void OnResizePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeTarget == ResizeTarget.None)
            return;

        _resizeTarget = ResizeTarget.None;
        if (sender is FrameworkElement element)
        {
            element.ReleasePointerCaptures();
            ResizeIndicatorFor(element).Opacity = 0;
        }
        NormalizeColumnWidths();
        e.Handled = true;
    }

    private void BeginResize(ResizeTarget target, object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        _resizeTarget = target;
        _resizeStartX = e.GetCurrentPoint(this).Position.X;
        _resizeStartLeftWidth = ContestListColumn.ActualWidth;
        _resizeStartRightWidth = OfficialStandingColumn.ActualWidth;
        element.CapturePointer(e.Pointer);
        ResizeIndicatorFor(element).Opacity = 0.85;
        e.Handled = true;
    }

    private void SetContestListWidth(double width)
    {
        ContestListColumn.Width = new GridLength(Clamp(width, MinContestListWidth, MaxLeftWidth()));
    }

    private void SetStandingWidth(double width)
    {
        OfficialStandingColumn.Width = new GridLength(Clamp(width, MinStandingWidth, MaxRightWidth()));
    }

    private void NormalizeColumnWidths()
    {
        WorkspaceGrid.Width = Math.Max(732, ActualWidth);
        _compactLayout = ActualWidth < 1180;
        CompactPanelTabs.Visibility = _compactLayout ? Visibility.Visible : Visibility.Collapsed;
        RightResizeHandle.Visibility = _compactLayout ? Visibility.Collapsed : Visibility.Visible;
        StandingDividerColumn.Width = new GridLength(_compactLayout ? 0 : 6);
        OfficialStandingColumn.MinWidth = _compactLayout ? 0 : MinStandingWidth;
        if (_compactLayout)
        {
            OfficialStandingColumn.Width = new GridLength(0);
            Grid.SetColumn(StandingPanel, 2);
            DetailPanel.Margin = StandingPanel.Margin = new Thickness(0, 70, 24, 24);
            ContestListColumn.Width = new GridLength(Clamp(CurrentPixelWidth(ContestListColumn, 280),
                MinContestListWidth, Math.Min(MaxContestListWidth, ActualWidth - MinDetailWidth - 6)));
            UpdateCompactPanel();
            return;
        }
        Grid.SetColumn(StandingPanel, 4);
        DetailPanel.Margin = new Thickness(0, 24, 0, 24);
        StandingPanel.Margin = new Thickness(0, 24, 24, 24);
        DetailPanel.Visibility = StandingPanel.Visibility = Visibility.Visible;
        SetContestListWidth(CurrentPixelWidth(ContestListColumn, 330));
        SetStandingWidth(CurrentPixelWidth(OfficialStandingColumn, 360));
    }

    private void OnDetailPanelClick(object sender, RoutedEventArgs e)
    {
        _showCompactStandings = false;
        UpdateCompactPanel();
    }
    private void OnStandingPanelClick(object sender, RoutedEventArgs e)
    {
        _showCompactStandings = true;
        UpdateCompactPanel();
        OnOfficialStandingsLoaded();
    }
    private void UpdateCompactPanel()
    {
        DetailPanelTab.IsChecked = !_showCompactStandings;
        StandingPanelTab.IsChecked = _showCompactStandings;
        DetailPanel.Visibility = !_compactLayout || !_showCompactStandings ? Visibility.Visible : Visibility.Collapsed;
        StandingPanel.Visibility = !_compactLayout || _showCompactStandings ? Visibility.Visible : Visibility.Collapsed;
    }

    private double MaxLeftWidth()
        => _compactLayout ? Math.Max(MinContestListWidth, Math.Min(MaxContestListWidth, ActualWidth - MinDetailWidth - 6)) : Math.Max(
            MinContestListWidth,
            Math.Min(
                MaxContestListWidth,
                AvailableContentWidth() - MinDetailWidth - CurrentPixelWidth(OfficialStandingColumn, 360)));

    private double MaxRightWidth()
        => Math.Max(
            MinStandingWidth,
            Math.Min(
                MaxStandingWidth,
                AvailableContentWidth() - MinDetailWidth - CurrentPixelWidth(ContestListColumn, 330)));

    private double AvailableContentWidth()
        => Math.Max(MinContestListWidth + MinDetailWidth + MinStandingWidth, ActualWidth - 12);

    private static double Clamp(double value, double min, double max)
        => Math.Min(Math.Max(value, min), Math.Max(min, max));

    private static double CurrentPixelWidth(ColumnDefinition column, double fallback)
        => column.ActualWidth > 0
            ? column.ActualWidth
            : column.Width.IsAbsolute ? column.Width.Value : fallback;

    private Rectangle ResizeIndicatorFor(FrameworkElement element)
        => element.Name == "LeftResizeHandle" ? LeftResizeIndicator : RightResizeIndicator;

    private enum ResizeTarget
    {
        None,
        Left,
        Right,
    }
}
