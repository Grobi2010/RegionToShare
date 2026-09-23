using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RegionToShare;

/// <summary>
/// Converts a color string like "SteelBlue" or "#FF4682B4" into a brush; invalid colors become transparent.
/// </summary>
public class ColorBrushConverter : IValueConverter
{
    public static readonly ColorBrushConverter Default = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return TryParseColor(value as string, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return DependencyProperty.UnsetValue;
    }

    public static bool TryParseColor(string? value, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
