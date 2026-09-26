using System.Globalization;

namespace Stakeout.Localization;

/// <summary>
/// Chooses and applies the UI language.
///
/// Architecture:
///   * All user-visible text lives in Localization/Strings.resx (Russian = neutral,
///     compiled into STAKEOUT.Core) and is read through the generated, strongly
///     typed <see cref="Strings"/> class — from C# as Strings.Key, from XAML as
///     {x:Static loc:Strings.Key}, so a missing key is a build error.
///   * Another language is a satellite resx, e.g. Strings.en.resx. A language is
///     considered available if it is the default or a satellite exists for it —
///     adding a language needs no code change.
///   * Selection order: "--lang xx" / "--lang=xx" argument, then the
///     STAKEOUT_LANG environment variable, then the default (Russian).
///   * The culture is applied once at startup, before any window or view model is
///     created; switching language takes effect on next launch.
/// </summary>
public static class LocalizationManager
{
    public const string EnvironmentVariable = "STAKEOUT_LANG";

    /// <summary>Default UI language (the neutral resources).</summary>
    public static CultureInfo DefaultCulture { get; } = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>
    /// Pick the UI culture from command-line args and an environment value,
    /// falling back to <see cref="DefaultCulture"/> for missing or unavailable languages.
    /// </summary>
    public static CultureInfo Resolve(IReadOnlyList<string>? args, string? environmentValue)
    {
        if (TryMatch(ReadLangArgument(args), out var fromArgs)) return fromArgs;
        if (TryMatch(environmentValue, out var fromEnv)) return fromEnv;
        return DefaultCulture;
    }

    /// <summary>True if <paramref name="tag"/> names a language we have strings for.</summary>
    public static bool TryMatch(string? tag, out CultureInfo culture)
    {
        culture = DefaultCulture;
        if (string.IsNullOrWhiteSpace(tag)) return false;

        CultureInfo requested;
        try
        {
            requested = CultureInfo.GetCultureInfo(tag.Trim());
        }
        catch (CultureNotFoundException)
        {
            return false;
        }

        if (!IsAvailable(requested)) return false;
        culture = requested;
        return true;
    }

    /// <summary>Default language, or a language that has a satellite resource assembly.</summary>
    /// <remarks>
    /// Probes for the satellite assembly itself rather than asking
    /// ResourceManager.GetResourceSet(culture, tryParents: false): after any
    /// fallback lookup (e.g. reading a string while the UI culture is de-DE) the
    /// ResourceManager caches the neutral Russian set under "de", which would make
    /// an untranslated language look available depending on lookup history.
    /// </remarks>
    public static bool IsAvailable(CultureInfo culture)
    {
        if (culture.TwoLetterISOLanguageName == DefaultCulture.TwoLetterISOLanguageName) return true;
        if (culture.Equals(CultureInfo.InvariantCulture)) return false;

        var neutral = culture.IsNeutralCulture ? culture : culture.Parent;
        try
        {
            typeof(Strings).Assembly.GetSatelliteAssembly(neutral);
            return true;
        }
        catch
        {
            return false; // FileNotFoundException when no satellite ships for this language
        }
    }

    /// <summary>
    /// Apply <paramref name="culture"/> process-wide (UI text and number/date formats).
    /// Call once at startup, before creating any UI.
    /// </summary>
    public static void Apply(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private static string? ReadLangArgument(IReadOnlyList<string>? args)
    {
        if (args == null) return null;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase)) return a["--lang=".Length..];
            if (a.Equals("--lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count) return args[i + 1];
        }
        return null;
    }
}
