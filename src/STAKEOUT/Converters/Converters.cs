using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stakeout.Core;

namespace Stakeout.Converters;

/// <summary>true → Visible, false → Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>true → Collapsed, false → Visible (inverse of the above).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>Non-empty string → Visible, else Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Enum value ⇄ "is this the one named by the parameter" (navigation radio
/// buttons). Checking a button writes its enum value back; unchecking writes
/// nothing, so the group never leaves the source without a value.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null && string.Equals(value.ToString(), parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string name && targetType.IsEnum && Enum.TryParse(targetType, name, out var result)
            ? result
            : Binding.DoNothing;
}

/// <summary>CPU temperature bar colour: hot (>=85 °C) → bright red, else accent.</summary>
public sealed class TempHotToBrushConverter : IValueConverter
{
    // Shared frozen brushes: the bar re-evaluates on every 3 s poll.
    private static readonly Brush Hot = NotificationKindToBrushConverter.Frozen(0xC4, 0x1E, 0x1E);
    private static readonly Brush Normal = NotificationKindToBrushConverter.Frozen(0x9B, 0x1B, 0x1B);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Hot : Normal;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Resolves a resource key (e.g. "Logo.chrome") to an <see cref="ImageSource"/>
/// from application resources. Used to bind vector logos by key from view models.
/// </summary>
public sealed class ResourceKeyToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key ? Application.Current?.TryFindResource(key) as ImageSource : null;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Log status colour: error → red, success → green, info → secondary text.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    // Frozen, shared brushes: no per-line allocations in the virtualized list.
    private static readonly Brush ErrorBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x45, 0x3A)));
    private static readonly Brush SuccessBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x5C, 0xB8, 0x5C)));
    private static readonly Brush InfoBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        Models.LogLevel.Error => ErrorBrush,
        Models.LogLevel.Success => SuccessBrush,
        _ => InfoBrush,
    };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}

/// <summary>Toast accent colour by notification kind.</summary>
public sealed class NotificationKindToBrushConverter : IValueConverter
{
    private static readonly Brush Success = Frozen(0x2E, 0x7D, 0x32);
    private static readonly Brush Warning = Frozen(0xC8, 0x8A, 0x12);
    private static readonly Brush Error = Frozen(0xC4, 0x1E, 0x1E);
    private static readonly Brush Info = Frozen(0x3A, 0x3A, 0x3A);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        NotificationKind.Success => Success,
        NotificationKind.Warning => Warning,
        NotificationKind.Error => Error,
        _ => Info,
    };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

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

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Services.UwpCategory cat && Map.TryGetValue(cat, out var b) ? b : Map[Services.UwpCategory.Other];
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// UWP logo path → small, fully loaded, frozen bitmap. Decoded at 40 px (the
/// table shows 20 px, doubled for high DPI) instead of the asset's full size,
/// and read into memory at once (OnLoad) so no file handle is kept open in
/// WindowsApps, where it could get in the way of Remove-AppxPackage.
/// An unreadable or missing file yields no image (the letter tile shows).
/// </summary>
public sealed class IconPathToImageConverter : IValueConverter
{
    private const int DecodeSize = 40;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !System.IO.File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = DecodeSize;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException
                                   or ArgumentException or InvalidOperationException or UnauthorizedAccessException
                                   or System.Runtime.InteropServices.COMException or FileFormatException)
        {
            Logger.Log("UWP icon", "WARNING", $"{System.IO.Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
