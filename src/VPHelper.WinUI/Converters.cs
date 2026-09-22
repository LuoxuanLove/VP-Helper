using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;

namespace VPHelper.WinUI;

/// <summary>Converts a bool to Visibility (true → Visible). Pass "invert" as parameter to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = value is bool v && v;
        if (parameter is string s && string.Equals(s, "invert", StringComparison.OrdinalIgnoreCase))
            b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility vis && vis == Visibility.Visible;
}

/// <summary>Inverts a bool.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : value;
}

/// <summary>Converts a bool to FontWeight (true → Bold, false → Normal).</summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b && b ? FontWeights.Bold : FontWeights.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a bool to a Brush (true → accent fill, false → transparent). Used to highlight the
/// VP row inside the official standings list.
/// </summary>
public sealed class BoolToAccentBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            // Accent-tinted subtle fill — readable in both light and dark themes.
            return new SolidColorBrush(Windows.UI.Color.FromArgb(40, 0, 120, 212));
        }
        return new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)); // transparent
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>Maps official-binding state to a compact status-badge background.</summary>
public sealed class BoolToMappingBadgeBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var mapped = value is bool b && b;
        return mapped
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(44, 0, 214, 184))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(44, 255, 185, 0));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>Maps official-binding state to a readable status-badge foreground.</summary>
public sealed class BoolToMappingBadgeForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var mapped = value is bool b && b;
        return mapped
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 71, 255, 232))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 210, 104));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>Maps a selected tab-like button to an accent or neutral fill.</summary>
public sealed class BoolToCategoryButtonBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var selected = value is bool b && b;
        return selected
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 214, 184))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(28, 255, 255, 255));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>Maps a selected tab-like button to readable text color.</summary>
public sealed class BoolToCategoryButtonForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var selected = value is bool b && b;
        return selected
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 242, 242, 242));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a nullable string to Visibility (non-null non-empty → Visible, null/empty →
/// Collapsed). Used to show optional fields like Organization in standings rows.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is string s && !string.IsNullOrWhiteSpace(s)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
