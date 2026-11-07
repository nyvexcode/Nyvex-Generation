using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NyvexGeneration.Core;

namespace NyvexGeneration.Helpers;

internal static class BrushPalette
{
    public static SolidColorBrush Create(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static readonly SolidColorBrush Green = Create("#22C55E");
    public static readonly SolidColorBrush Slate = Create("#A8A3C7");
    public static readonly SolidColorBrush Amber = Create("#F59E0B");
    public static readonly SolidColorBrush Red = Create("#EF4444");
    public static readonly SolidColorBrush Violet = Create("#C084FC");
    public static readonly SolidColorBrush Muted = Create("#9A8FB8");
    public static readonly SolidColorBrush Blue = Create("#60A5FA");
}

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b && !b;
}

[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i && i == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(LookupStatus), typeof(Brush))]
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        LookupStatus.AccountFound => BrushPalette.Green,
        LookupStatus.NoPublicMatch => BrushPalette.Slate,
        LookupStatus.Inconclusive => BrushPalette.Amber,
        LookupStatus.ConnectionError => BrushPalette.Red,
        LookupStatus.Checking => BrushPalette.Violet,
        _ => BrushPalette.Muted
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(LogSeverity), typeof(Brush))]
public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        LogSeverity.Error => BrushPalette.Red,
        LogSeverity.Warning => BrushPalette.Amber,
        LogSeverity.Information => BrushPalette.Blue,
        _ => BrushPalette.Muted
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(ConnectionState), typeof(Brush))]
public sealed class ConnectionBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ConnectionState.Online => BrushPalette.Green,
        ConnectionState.RateLimited => BrushPalette.Amber,
        ConnectionState.Offline => BrushPalette.Red,
        _ => BrushPalette.Muted
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(string))]
public sealed class FavoriteGlyphConverter : IValueConverter
{
    // Segoe MDL2 Assets: E735 = estrela preenchida, E734 = estrela vazia.
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "" : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
