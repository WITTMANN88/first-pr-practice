using Stakeout.Core;
using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.Design;

/// <summary>
/// Realistic fake data for the XAML designer (d:DataContext). Deterministic, so
/// the designer shows the same picture every time, and it goes through the real
/// code paths (UwpCatalog categorisation, the log line format and parser), so
/// what the designer shows is what the app would show for such input.
///
/// Unit tests pin the properties the designer relies on: every UWP badge colour
/// appears, every log level appears, the temperature trace crosses the overheat
/// line. Never used at runtime.
/// </summary>
public static class DesignSamples
{
    private const string MsPublisher = "8wekyb3d8bbwe";

    /// <summary>Installed packages covering every <see cref="UwpCategory"/>, protected ones included.</summary>
    public static IReadOnlyList<UwpApp> UwpApps()
    {
        (string Name, string Version, string Publisher, double SizeMb)[] packages =
        {
            ("Microsoft.BingNews", "4.55.62231.0", MsPublisher, 38.4),
            ("king.com.CandyCrushSaga", "1.2690.3.0", "kgqvnymyfvs32", 212.7),
            ("SpotifyAB.SpotifyMusic", "1.244.455.0", "zpdnekdrzrea0", 164.2),
            ("Microsoft.GetHelp", "10.2409.22951.0", MsPublisher, 12.9),
            ("Microsoft.GamingApp", "2409.1001.14.0", MsPublisher, 97.5),
            ("Microsoft.MicrosoftSolitaireCollection", "4.21.9110.0", MsPublisher, 141.0),
            ("Microsoft.ZuneMusic", "11.2408.12.0", MsPublisher, 64.3),
            ("Microsoft.Windows.Photos", "2024.11090.18001.0", MsPublisher, 276.8),
            ("Microsoft.ScreenSketch", "11.2409.25.0", MsPublisher, 21.6),
            ("Microsoft.WindowsAlarms", "11.2408.6.0", MsPublisher, 9.8),
            ("5319275A.WhatsAppDesktop", "2.2436.3.0", "cv1g1gvanyjgm", 188.1),
            ("Microsoft.Whiteboard", "51.20916.4.0", MsPublisher, 73.0),
            ("Microsoft.WindowsStore", "22409.1401.5.0", MsPublisher, 48.2),
            ("Microsoft.DesktopAppInstaller", "1.23.1911.0", MsPublisher, 33.5),
            ("Microsoft.VCLibs.140.00.UWPDesktop", "14.0.33728.0", MsPublisher, 7.1),
        };

        return packages.Select(p =>
        {
            var app = UwpApp.FromIdentity(
                p.Name,
                $"{p.Name}_{p.Version}_x64__{p.Publisher}",
                $@"C:\Program Files\WindowsApps\{p.Name}_{p.Version}_x64__{p.Publisher}");
            app.SizeBytes = (long)(p.SizeMb * 1024 * 1024);
            return app;
        }).ToList();
    }

    /// <summary>A plausible session in the exact on-disk format (before encryption).</summary>
    public static IReadOnlyList<string> LogLines()
    {
        var t = new DateTime(2026, 9, 26, 21, 14, 3, DateTimeKind.Local);
        (int Sec, string Action, string Status, string? Detail)[] lines =
        {
            (0, "Logger", "OK", "Session started (Microsoft Windows NT 10.0.19045.0)"),
            (0, "App", "START", "elevated; ui=ru-RU"),
            (1, "TweakStateStore", "RECOVERED", "state restored from tweak-state.20260926T180211.4410032Z.json"),
            (2, "SysInfo.Load", "OK", "AMD Ryzen 7 5800X3D; 2 GPU; 32 ГБ"),
            (2, "SysInfo.Wmi", "TIMEOUT", "Win32_PhysicalMemory > 8 s, registry fallback used"),
            (9, "Tweak:mpo", "APPLIED", @"HKLM\SOFTWARE\Microsoft\Windows\Dwm OverlayTestMode = 5"),
            (12, "Tweak:hags", "APPLIED", "HwSchMode = 2 (restart required)"),
            (15, "Tweak:rawmouse", "WARNING", "RawMouseThrottleDuration: path not verified on this build"),
            (21, "Tweak:uac", "REVERTED", "EnableLUA = 1"),
            (30, "Yandex", "BLOCKED", "DisallowRun: browser.exe, yandex.exe, YandexDisk2.exe"),
            (41, "Uwp.List", "OK", "126 packages"),
            (47, "Uwp.Remove", "OK", "king.com.CandyCrushSaga (212.7 МБ)"),
            (48, "Uwp.Remove", "ACCESS_DENIED", "Microsoft.XboxGameCallableUI: 0x80073CFA"),
            (60, "Download", "OK", "7-Zip 24.08 x64 (1.6 МБ)"),
            (74, "Winget", "FAILED", "Discord.Discord: exit code 0x8A15000F"),
            (80, "Notify", "SUCCESS", "Discord установлен"),
            (95, "RevertAll", "PARTIAL", "4 of 5 reverted"),
            (96, "Explorer", "RESTARTED", null),
        };

        return lines
            .Select(l => EncryptedLogFile.FormatLine(t.AddSeconds(l.Sec), l.Action, l.Status, l.Detail))
            .ToList();
    }

    /// <summary>
    /// Two minutes of CPU package temperature (one reading per poll): idle, a
    /// gaming spike that crosses the overheat line, then cooling down.
    /// </summary>
    public static IReadOnlyList<double> CpuTemperatures() => new[]
    {
        46.0, 45.5, 46.2, 47.0, 46.4, 45.8, 47.3, 48.9, 52.4, 58.1,
        63.7, 68.2, 72.9, 76.4, 79.8, 82.3, 84.6, 86.9, 88.7, 89.4,
        88.1, 86.2, 84.9, 83.7, 84.4, 85.8, 84.1, 81.0, 77.2, 73.5,
        70.1, 67.4, 65.2, 63.9, 62.8, 61.5, 60.9, 61.7, 62.4, 61.8,
    };
}
