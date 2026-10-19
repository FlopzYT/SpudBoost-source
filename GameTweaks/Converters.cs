using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GameTweaks;

/// <summary>Collapses an element when the bound value is null or empty.</summary>
public sealed class NotNullToVisibility : IValueConverter
{
    public static NotNullToVisibility Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null or "" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
