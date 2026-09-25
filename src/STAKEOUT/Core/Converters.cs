using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Stakeout.Core;

/// <summary>true → Visible, false → Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => value is Visibility.Visible;
}

/// <summary>true → Collapsed, false → Visible (inverse of the above).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => value is Visibility.Collapsed;
}

/// <summary>Non-empty string → Visible, else Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => Binding.DoNothing;
}

/// <summary>CPU temperature bar colour: hot (>=85 °C) → bright red, else accent.</summary>
public sealed class TempHotToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true
            ? new SolidColorBrush(Color.FromRgb(0xC4, 0x1E, 0x1E))   // hot
            : new SolidColorBrush(Color.FromRgb(0x9B, 0x1B, 0x1B));  // normal accent
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Toast accent colour by kind.</summary>
public sealed class ToastKindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        ToastKind.Success => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
        ToastKind.Error => new SolidColorBrush(Color.FromRgb(0xC4, 0x1E, 0x1E)),
        _ => new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
    };
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}
