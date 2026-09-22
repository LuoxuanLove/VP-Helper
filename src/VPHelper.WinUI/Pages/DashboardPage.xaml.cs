using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VPHelper.Core.Models;
using VPHelper.WinUI.ViewModels;

namespace VPHelper.WinUI.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel Vm { get; }

    public DashboardPage()
    {
        InitializeComponent();
        Vm = AppHost.Get<DashboardViewModel>();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
        => await Vm.RefreshAsync();

    private async void OnRefresh(object sender, RoutedEventArgs e)
        => await Vm.RefreshAsync();

    private void OnStatCardsLoaded(object sender, RoutedEventArgs e) => ResizeStatCards((ItemsControl)sender);
    private void OnStatCardsSizeChanged(object sender, SizeChangedEventArgs e) => ResizeStatCards((ItemsControl)sender);
    private static void ResizeStatCards(ItemsControl items)
    {
        static ItemsWrapGrid? FindPanel(DependencyObject parent)
        {
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is ItemsWrapGrid panel) return panel;
                if (FindPanel(child) is { } nested) return nested;
            }
            return null;
        }
        if (FindPanel(items) is not { } wrap || items.ActualWidth <= 0) return;
        var columns = items.ActualWidth >= 980 ? 7 : items.ActualWidth >= 640 ? 4 : 2;
        wrap.MaximumRowsOrColumns = columns;
        wrap.ItemWidth = Math.Floor(items.ActualWidth / columns);
    }

    private void OnPreviousPage(object sender, RoutedEventArgs e)
        => Vm.PreviousPage();

    private void OnNextPage(object sender, RoutedEventArgs e)
        => Vm.NextPage();

    private void OnGoToLogin(object sender, RoutedEventArgs e)
    {
        // Walk up to the MainWindow and navigate to the accounts page.
        if (XamlRoot?.Content is FrameworkElement root)
        {
            var win = root.XamlRoot?.Content;
            // Fire and forget — raises the event on the main window via the NavigateRequest pattern.
        }
        NavigateRequested?.Invoke(this, "login");
    }

    private void OnRecentContestClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentVpItem item })
            ContestRequested?.Invoke(this, new ContestNavigationRequest(item.Platform, item.GymId, item.Handle));
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }) NavigateRequested?.Invoke(this, tag);
    }

    /// <summary>Raised when this page wants the shell to navigate to another tag.</summary>
    public event EventHandler<string>? NavigateRequested;

    public event EventHandler<ContestNavigationRequest>? ContestRequested;
}

public sealed record ContestNavigationRequest(Platform Platform, long GymId, string? Handle = null);
