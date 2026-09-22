using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using VPHelper.WinUI.ViewModels;

namespace VPHelper.WinUI.Pages;

public sealed partial class OfficialPage : Page
{
    public OfficialViewModel Vm { get; }
    private MappingRowItem? _pickerRow;
    private bool _suppressPickerTextChanged;

    public OfficialPage()
    {
        Vm = AppHost.Get<OfficialViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            CloseOfficialPicker(clearSuggestions: true);
            await Vm.LoadAsync();
            CloseOfficialPicker(clearSuggestions: true);
        };
        Unloaded += (_, _) => CloseOfficialPicker(clearSuggestions: true);
        SizeChanged += (_, _) => { if (OfficialPickerPopup.IsOpen) CloseOfficialPicker(clearSuggestions: true); };
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Vm.CancelSync();

    private async void OnAutoMatch(object sender, RoutedEventArgs e)
    {
        CloseOfficialPicker(clearSuggestions: true);
        await Vm.AutoMatchAllAsync();
    }

    private void OnPagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsInsidePopup(e.OriginalSource))
            CloseOfficialPicker(clearSuggestions: true);
    }

    private void OnOverviewListPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsInsidePopup(e.OriginalSource))
            CloseOfficialPicker(clearSuggestions: true);
    }

    private void OnOverviewListPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        CloseOfficialPicker(clearSuggestions: true);
    }

    private void OnOpenOfficialPicker(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MappingRowItem row } || row.IsConfirmed)
            return;

        _pickerRow = row;
        Vm.RefreshRowOfficialSuggestions(row, row.OfficialSearchText, preserveSelection: true);
        OfficialPickerSuggestions.ItemsSource = row.OfficialSuggestions;
        OfficialPickerSuggestions.SelectedIndex = -1;

        _suppressPickerTextChanged = true;
        OfficialPickerTextBox.Text = row.OfficialSearchText;
        _suppressPickerTextChanged = false;

        PlaceOfficialPicker(sender as FrameworkElement);
        OfficialPickerPopup.IsOpen = true;
        OfficialPickerTextBox.Focus(FocusState.Programmatic);
        OfficialPickerTextBox.SelectAll();
    }

    private void OnOfficialPickerTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPickerTextChanged || _pickerRow == null)
            return;

        _pickerRow.OfficialSearchText = OfficialPickerTextBox.Text;
        Vm.RefreshRowOfficialSuggestions(_pickerRow, OfficialPickerTextBox.Text);
        OfficialPickerSuggestions.SelectedIndex = -1;
    }

    private void OnOfficialPickerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseOfficialPicker(clearSuggestions: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            // Enter only commits an explicitly selected result. Never silently bind the first
            // search result, because similarly named contests are common.
            CommitPickerSelection(OfficialPickerSuggestions.SelectedItem as CandidateItem);
            e.Handled = true;
        }
    }

    private void OnClearOfficialPickerText(object sender, RoutedEventArgs e)
    {
        OfficialPickerTextBox.Text = string.Empty;
        OfficialPickerTextBox.Focus(FocusState.Programmatic);
    }

    private void OnCloseOfficialPicker(object sender, RoutedEventArgs e)
    {
        CloseOfficialPicker(clearSuggestions: true);
    }

    private void OnOfficialPickerSuggestionClick(object sender, ItemClickEventArgs e)
    {
        CommitPickerSelection(e.ClickedItem as CandidateItem);
    }

    private void CommitPickerSelection(CandidateItem? candidate)
    {
        if (_pickerRow == null || candidate == null)
            return;

        Vm.ChooseRowOfficialSuggestion(_pickerRow, candidate);
        CloseOfficialPicker(clearSuggestions: true);
    }

    private void OnOfficialPickerClosed(object sender, object e)
    {
        CloseOfficialPicker(clearSuggestions: true, updatePopup: false);
    }

    private async void OnAcceptRow(object sender, RoutedEventArgs e)
    {
        CloseOfficialPicker(clearSuggestions: true);
        if (sender is FrameworkElement { DataContext: MappingRowItem row })
            await Vm.AcceptOverviewRowAsync(row);
    }

    private async void OnClearRow(object sender, RoutedEventArgs e)
    {
        CloseOfficialPicker(clearSuggestions: true);
        if (sender is FrameworkElement { DataContext: MappingRowItem row })
            await Vm.ClearOverviewRowAsync(row);
    }

    private async void OnContestLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void PlaceOfficialPicker(FrameworkElement? anchor)
    {
        if (anchor == null)
            return;

        var transform = anchor.TransformToVisual(Root);
        var point = transform.TransformPoint(new Windows.Foundation.Point(0, anchor.ActualHeight + 4));
        if (OfficialPickerPopup.Child is not FrameworkElement content) return;
        var width = Math.Min(600, Math.Max(240, Root.ActualWidth - 32));
        var height = Math.Min(420, Math.Max(160, Root.ActualHeight - 32));
        content.Width = width;
        content.MaxHeight = height;
        OfficialPickerPopup.HorizontalOffset = Math.Clamp(point.X, 0, Math.Max(0, Root.ActualWidth - width));
        OfficialPickerPopup.VerticalOffset = Math.Clamp(point.Y, 0, Math.Max(0, Root.ActualHeight - height));
    }

    private void CloseOfficialPicker(bool clearSuggestions, bool updatePopup = true)
    {
        if (updatePopup && OfficialPickerPopup.IsOpen)
            OfficialPickerPopup.IsOpen = false;

        if (clearSuggestions)
            Vm.ClearAllRowSuggestions();

        OfficialPickerSuggestions.ItemsSource = null;
        _pickerRow = null;
        _suppressPickerTextChanged = true;
        OfficialPickerTextBox.Text = string.Empty;
        _suppressPickerTextChanged = false;
        Root.Focus(FocusState.Programmatic);
    }

    private static bool IsInsidePopup(object? source)
    {
        for (var current = source as DependencyObject; current != null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is Popup)
                return true;
        }

        return false;
    }
}
