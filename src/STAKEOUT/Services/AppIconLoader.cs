using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stakeout.Core;
using Stakeout.Infrastructure;

namespace Stakeout.Services;

/// <summary>
/// Icon for a row of the Apps table, off the UI thread: a UWP logo file (PNG)
/// decoded small, or a desktop program's DisplayIcon — an icon inside an
/// .exe/.dll (by index or resource id) or an .ico. The result is frozen, so it
/// can be created on a worker thread and shown on the UI thread. Null when the
/// file is missing or unreadable (the letter tile shows instead).
/// </summary>
public static class AppIconLoader
{
    /// <summary>Rows show 20 px icons; decode at twice that for high DPI.</summary>
    private const int DecodeSize = 40;

    public static ImageSource? Load(string? path, int index)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var file = path.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(file) || !File.Exists(file)) return null;

        var ext = Path.GetExtension(file);
        var fromResources = ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase);
        try
        {
            return fromResources ? FromIconResource(file, index) : FromImageFile(file);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException
                                   or InvalidOperationException or UnauthorizedAccessException or COMException or FileFormatException)
        {
            Logger.Log("Apps.Icon", "WARNING", $"{Path.GetFileName(file)}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// PNG / JPG: read into memory at once (OnLoad) so no file handle stays open
    /// in WindowsApps, where it could get in the way of Remove-AppxPackage.
    /// </summary>
    private static BitmapImage FromImageFile(string file)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(file, UriKind.Absolute);
        image.DecodePixelWidth = DecodeSize;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource? FromIconResource(string file, int index)
    {
        var name = Marshal.StringToHGlobalUni(file);
        var icon = IntPtr.Zero;
        try
        {
            if (NativeMethods.ExtractIconExW(name, index, out icon, IntPtr.Zero, 1) == 0 || icon == IntPtr.Zero)
                return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (icon != IntPtr.Zero) _ = NativeMethods.DestroyIcon(icon);
            Marshal.FreeHGlobal(name);
        }
    }
}
