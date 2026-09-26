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

/// <summary>
/// Resolves a resource key (e.g. "Logo.chrome") to an <see cref="ImageSource"/>
/// from application resources. Used to bind vector logos by key from view models.
/// </summary>
public sealed class ResourceKeyToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
        => value is string key ? Application.Current?.TryFindResource(key) as ImageSource : null;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Log status colour: error → red, success → green, info → secondary text.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    // Frozen, shared brushes: no per-line allocations in the virtualized list.
    private static readonly Brush ErrorBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x45, 0x3A)));
    private static readonly Brush SuccessBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x5C, 0xB8, 0x5C)));
    private static readonly Brush InfoBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));

    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        Models.LogLevel.Error => ErrorBrush,
        Models.LogLevel.Success => SuccessBrush,
        _ => InfoBrush,
    };
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}

/// <summary>Toast accent colour by notification kind.</summary>
public sealed class NotificationKindToBrushConverter : IValueConverter
{
    private static readonly Brush Success = Frozen(0x2E, 0x7D, 0x32);
    private static readonly Brush Warning = Frozen(0xC8, 0x8A, 0x12);
    private static readonly Brush Error = Frozen(0xC4, 0x1E, 0x1E);
    private static readonly Brush Info = Frozen(0x3A, 0x3A, 0x3A);

    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        NotificationKind.Success => Success,
        NotificationKind.Warning => Warning,
        NotificationKind.Error => Error,
        _ => Info,
    };
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;

    internal static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Badge colour per UWP category. Junk stands out in the accent red; protected
/// system packages are muted; the rest get distinct, calm hues.
/// </summary>
public sealed class UwpCategoryToBrushConverter : IValueConverter
{
    private static readonly Dictionary<Services.UwpCategory, Brush> Map = new()
    {
        [Services.UwpCategory.Bloatware] = NotificationKindToBrushConverter.Frozen(0xC4, 0x1E, 0x1E),
        [Services.UwpCategory.Games] = NotificationKindToBrushConverter.Frozen(0x4C, 0xAF, 0x50),
        [Services.UwpCategory.Media] = NotificationKindToBrushConverter.Frozen(0xC8, 0x8A, 0x12),
        [Services.UwpCategory.Utilities] = NotificationKindToBrushConverter.Frozen(0x26, 0xA6, 0x9A),
        [Services.UwpCategory.ThirdParty] = NotificationKindToBrushConverter.Frozen(0x8E, 0x6C, 0xD8),
        [Services.UwpCategory.Other] = NotificationKindToBrushConverter.Frozen(0x78, 0x78, 0x78),
        [Services.UwpCategory.System] = NotificationKindToBrushConverter.Frozen(0x5C, 0x7C, 0xA8),
    };

    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is Services.UwpCategory cat && Map.TryGetValue(cat, out var b) ? b : Map[Services.UwpCategory.Other];
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}
