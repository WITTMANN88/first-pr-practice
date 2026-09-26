using System.Text.RegularExpressions;

namespace Stakeout.Services;

/// <summary>Category shown as a badge in the UWP table. Declaration order = sort order.</summary>
public enum UwpCategory
{
    Bloatware,   // preinstalled / sponsored junk — the only category "select junk" picks
    Games,       // Xbox ecosystem and Microsoft games
    Media,       // photos, music, video, camera
    Utilities,   // small built-in tools
    ThirdParty,  // non-Microsoft packages we do not recognise
    Other,       // Microsoft packages we do not recognise
    System,      // frameworks, shell components, codecs, protected apps
}

/// <summary>
/// Knowledge base for UWP (Appx) packages: which are protected from removal and
/// which category each belongs to. Matching is by package identity name
/// (e.g. "Microsoft.BingNews", "king.com.CandyCrushSaga"), case-insensitive.
///
/// Evaluation order matters:
///   1. Protected packages → System (a protected app is never labelled junk).
///   2. Specific known apps (bloatware, games, media, utilities) — before the
///      generic system patterns, so e.g. "Microsoft.Windows.Photos" is Media,
///      not System via "Microsoft.Windows.*".
///   3. Generic system patterns (frameworks, shell hosts, codecs, GUID-named apps).
///   4. Fallback by publisher prefix: "Microsoft*" → Other, anything else → ThirdParty.
/// </summary>
public static class UwpCatalog
{
    /// <summary>
    /// Unsafe to remove: the Store and its purchase flow, App Installer (winget),
    /// Calculator, shared runtime libraries other apps depend on, and core shell
    /// / security / sign-in components. Matched as substrings.
    /// </summary>
    private static readonly string[] CriticalMarkers =
    {
        "WindowsStore", "StorePurchaseApp", "DesktopAppInstaller", "WindowsCalculator",
        "VCLibs", "NET.Native.Framework", "NET.Native.Runtime", "UI.Xaml",
        "Microsoft.Services.Store", "StoreExperienceHost", "SecHealthUI",
        "ShellExperienceHost", "Windows.StartMenuExperienceHost",
        "Microsoft.AAD.BrokerPlugin", "Microsoft.AccountsControl", "WindowsAppRuntime",
        "XboxGameCallableUI", // unremovable system component despite the "Xbox" name
    };

    private static readonly (UwpCategory Category, string[] Patterns)[] SpecificRules =
    {
        (UwpCategory.Bloatware, new[]
        {
            // Microsoft preinstalled extras
            "Microsoft.Bing*", "Microsoft.GetHelp", "Microsoft.Getstarted", "Microsoft.MicrosoftOfficeHub",
            "Microsoft.Office.OneNote", "Microsoft.People", "Microsoft.SkypeApp", "Microsoft.WindowsFeedbackHub",
            "Microsoft.WindowsMaps", "Microsoft.MixedReality.Portal", "Microsoft.Microsoft3DViewer",
            "Microsoft.Print3D", "Microsoft.MSPaint", "Microsoft.OneConnect", "Microsoft.Wallet",
            "Microsoft.Messaging", "Microsoft.CommsPhone", "Microsoft.ConnectivityStore",
            "Microsoft.PowerAutomateDesktop", "Microsoft.549981C3F5F10", "Microsoft.Windows.DevHome",
            "Microsoft.Copilot*", "Microsoft.Windows.Ai.Copilot*", "MicrosoftWindows.Client.WebExperience",
            "MicrosoftCorporationII.MicrosoftFamily", "MicrosoftTeams", "MSTeams", "Clipchamp.Clipchamp",
            // sponsored third-party apps and games pushed by Windows
            "king.com.*", "*CandyCrush*", "*BubbleWitch*", "*MarchofEmpires*", "*Asphalt*",
            "*Disney*", "SpotifyAB.SpotifyMusic", "Facebook.*", "*Twitter*", "*.Netflix", "*TikTok*",
            "*Instagram*", "AmazonVideo.PrimeVideo", "*Flipboard*", "*Duolingo*", "*PandoraMediaInc*",
            "*HiddenCity*", "*Hulu*", "*McAfee*",
        }),
        (UwpCategory.Games, new[]
        {
            "Microsoft.XboxApp", "Microsoft.GamingApp", "Microsoft.GamingServices", "Microsoft.Xbox*",
            "Microsoft.MicrosoftSolitaireCollection", "Microsoft.MicrosoftMahjong",
            "Microsoft.MicrosoftMinesweeper", "Microsoft.Minecraft*",
        }),
        (UwpCategory.Media, new[]
        {
            "Microsoft.ZuneMusic", "Microsoft.ZuneVideo", "Microsoft.Windows.Photos", "Microsoft.WindowsCamera",
            "Microsoft.WindowsSoundRecorder", "Microsoft.Photos.*",
        }),
        (UwpCategory.Utilities, new[]
        {
            "Microsoft.WindowsAlarms", "Microsoft.WindowsNotepad", "Microsoft.Paint", "Microsoft.ScreenSketch",
            "Microsoft.MicrosoftStickyNotes", "Microsoft.WindowsTerminal", "Microsoft.PowerShell",
            "Microsoft.Todos", "MicrosoftCorporationII.QuickAssist",
        }),
    };

    private static readonly string[] SystemPatterns =
    {
        "Microsoft.Windows.*", "MicrosoftWindows.*", "Windows.*", "Microsoft.NET.*",
        "Microsoft.Advertising.Xaml", "Microsoft.*Extension", "Microsoft.*Extensions",
        "Microsoft.LockApp", "Microsoft.ECApp", "Microsoft.CredDialogHost", "Microsoft.AsyncTextService",
        "Microsoft.BioEnrollment", "Microsoft.Win32WebViewHost", "Microsoft.MicrosoftEdge*",
        "NcsiUwpApp",
    };

    /// <summary>Windows ships several system apps named by a bare GUID (File Explorer, file picker…).</summary>
    private static readonly Regex GuidName = new(
        "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Compiled once. Order of SpecificRules preserved.
    private static readonly (UwpCategory Category, Regex[] Matchers)[] CompiledRules =
        SpecificRules.Select(r => (r.Category, r.Patterns.Select(Glob).ToArray())).ToArray();
    private static readonly Regex[] CompiledSystem = SystemPatterns.Select(Glob).ToArray();

    /// <summary>True for packages that must never be removed or auto-selected.</summary>
    public static bool IsCritical(string? name)
        => !string.IsNullOrEmpty(name) &&
           CriticalMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>Category for a package identity name (never throws).</summary>
    public static UwpCategory Categorize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return UwpCategory.Other;
        if (IsCritical(name)) return UwpCategory.System;

        foreach (var (category, matchers) in CompiledRules)
            if (matchers.Any(m => m.IsMatch(name))) return category;

        if (CompiledSystem.Any(m => m.IsMatch(name)) || GuidName.IsMatch(name)) return UwpCategory.System;

        return name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
            ? UwpCategory.Other
            : UwpCategory.ThirdParty;
    }

    /// <summary>Translate a "*"-only glob into an anchored, case-insensitive regex.</summary>
    private static Regex Glob(string pattern)
        => new("^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$",
               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
