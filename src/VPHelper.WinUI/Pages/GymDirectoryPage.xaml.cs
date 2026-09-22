using System;
using Microsoft.UI.Xaml;
using System.Linq;
using VPHelper.Core.Models;
using Microsoft.UI.Xaml.Controls;
using VPHelper.WinUI.ViewModels;

namespace VPHelper.WinUI.Pages;

public sealed partial class GymDirectoryPage : Page
{
    public GymDirectoryViewModel Vm { get; }

    public GymDirectoryPage()
    {
        Vm = AppHost.Get<GymDirectoryViewModel>();
        var category = Vm.SelectedCategory;
        InitializeComponent();
        CategoryPicker.SelectedItem = CategoryPicker.Items.Cast<ComboBoxItem>()
            .First(i => (string)i.Tag == category.ToString());
        Loaded += async (_, _) => await Vm.LoadAsync();
    }

    public event EventHandler<ContestNavigationRequest>? ContestRequested;
    private void OnDirectoryViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DirectoryTable != null) DirectoryTable.Width = Math.Max(850, e.NewSize.Width);
    }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await Vm.LoadAsync(refreshCatalog: true);
    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm != null && sender is ComboBox { SelectedItem: ComboBoxItem { Tag: string tag } } &&
            Enum.TryParse<GymDirectoryCategory>(tag, out var category)) Vm.SelectedCategory = category;
    }
    private void OnResetFilters(object sender, RoutedEventArgs e)
    {
        Vm.SearchText = string.Empty;
        Vm.ParticipationFilter = 0;
        CategoryPicker.SelectedIndex = 0;
    }
    private void OnResultsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GymDirectoryItem item } anchor) return;
        var flyout = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Left };
        foreach (var run in item.Runs)
        {
            if (run.Kind == ParticipationKind.Practice && item.Runs.Any(r => r.Kind == ParticipationKind.Virtual &&
                r.Platform == run.Platform && r.GymId == run.GymId && r.Handle == run.Handle)) continue;
            var platform = run.Platform == Platform.Qoj ? "QOJ" : "CF";
            var kind = run.Kind == ParticipationKind.Virtual ? "VP" : "练习";
            var rank = run.Kind != ParticipationKind.Virtual ? "不计入 VP 排名"
                : run.Rank.HasValue ? $"#{run.Rank} / {run.TotalTeams}"
                : string.IsNullOrEmpty(run.OfficialId) ? "待关联官方比赛" : "打开详情加载榜单";
            var menuItem = new MenuFlyoutItem { Text = $"{platform} {run.GymId} · {run.Handle} · {kind} {run.Solved} 题 · {rank}" };
            menuItem.Click += (_, _) => ContestRequested?.Invoke(this, new ContestNavigationRequest(run.Platform, run.GymId, run.Handle));
            flyout.Items.Add(menuItem);
        }
        flyout.ShowAt(anchor);
    }

    private async void OnGymUrlClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrEmpty(url))
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    private async void OnQojUrlClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrEmpty(url))
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    private async void OnRanklandUrlClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrEmpty(url))
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

}
