using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Stakeout.Localization;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Localization;

public class StringsTests
{
    [Fact]
    public void Russian_IsTheDefaultLanguage()
    {
        using var _ = new CultureScope("ru-RU");
        Assert.Equal("Система", Strings.Nav_System);
        Assert.Equal("Предустановленный мусор", Strings.UwpCategory_Bloatware);
    }

    [Fact]
    public void English_ComesFromTheSatelliteAssembly()
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal("System", Strings.Nav_System);
        Assert.Equal("Preinstalled junk", Strings.UwpCategory_Bloatware);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    public void UnsupportedLanguage_FallsBackToRussian(string culture)
    {
        using var _ = new CultureScope(culture);
        Assert.Equal("Система", Strings.Nav_System);
    }

    [Fact]
    public void FormatResources_ProduceLocalizedText()
    {
        using (new CultureScope("ru-RU"))
            Assert.Equal("UAC: включено", string.Format(CultureInfo.CurrentCulture, Strings.Tweaks_Enabled, "UAC"));
        using (new CultureScope("en-US"))
            Assert.Equal("UAC: enabled", string.Format(CultureInfo.CurrentCulture, Strings.Tweaks_Enabled, "UAC"));
    }
}

/// <summary>
/// Guards that keep translations complete and safe. Adding a key to Strings.resx
/// without translating it (or dropping a {0}) fails the build's test run.
/// </summary>
public class ResourceParityTests
{
    private static readonly CultureInfo[] Satellites = { CultureInfo.GetCultureInfo("en") };

    private static Dictionary<string, string> Load(CultureInfo culture)
    {
        var set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
                  ?? throw new InvalidOperationException($"No resources for '{culture.Name}'.");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static Dictionary<string, string> Neutral => Load(CultureInfo.InvariantCulture);

    public static IEnumerable<object[]> SatelliteCultures => Satellites.Select(c => new object[] { c.Name });

    [Fact]
    public void Neutral_HasStrings_AndNoneAreEmpty()
    {
        var neutral = Neutral;
        Assert.True(neutral.Count > 100, $"expected the full catalogue, got {neutral.Count} keys");
        Assert.All(neutral, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"empty value for '{kv.Key}'"));
    }

    [Theory]
    [MemberData(nameof(SatelliteCultures))]
    public void Satellite_HasExactlyTheSameKeys(string culture)
    {
        var translated = Load(CultureInfo.GetCultureInfo(culture));
        var neutral = Neutral;
        Assert.Empty(neutral.Keys.Except(translated.Keys));   // untranslated keys
        Assert.Empty(translated.Keys.Except(neutral.Keys));   // stale keys
    }

    [Theory]
    [MemberData(nameof(SatelliteCultures))]
    public void Satellite_HasNoEmptyValues(string culture)
    {
        Assert.All(Load(CultureInfo.GetCultureInfo(culture)),
            kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"empty value for '{kv.Key}'"));
    }

    [Theory]
    [MemberData(nameof(SatelliteCultures))]
    public void Satellite_KeepsTheSamePlaceholders(string culture)
    {
        var translated = Load(CultureInfo.GetCultureInfo(culture));
        foreach (var (key, value) in Neutral)
            Assert.True(Placeholders(value).SetEquals(Placeholders(translated[key])),
                $"placeholders differ for '{key}': '{value}' vs '{translated[key]}'");
    }

    [Fact]
    public void EveryFormatString_FormatsInEveryLanguage()
    {
        foreach (var culture in Satellites.Prepend(CultureInfo.GetCultureInfo("ru-RU")))
        {
            var values = culture.TwoLetterISOLanguageName == "ru" ? Neutral : Load(culture);
            foreach (var (key, format) in values)
            {
                var args = Enumerable.Range(0, Placeholders(format).DefaultIfEmpty(-1).Max() + 1)
                                     .Select(i => (object)(i + 1.5)).ToArray();
                var ex = Record.Exception(() => string.Format(culture, format, args));
                Assert.True(ex is null, $"'{key}' [{culture.Name}] does not format: {ex?.Message}");
            }
        }
    }

    private static HashSet<int> Placeholders(string s)
        => Regex.Matches(s, @"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}")
                .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .ToHashSet();
}

public class LocalizationManagerResolveTests
{
    [Theory]
    [InlineData(new[] { "--lang=en" }, null, "en")]
    [InlineData(new[] { "--lang", "en" }, null, "en")]
    [InlineData(new[] { "--LANG=en-US" }, null, "en-US")]
    [InlineData(new[] { "--other", "--lang=en" }, null, "en")]
    [InlineData(new string[0], "en", "en")]
    [InlineData(new[] { "--lang=ru" }, "en", "ru")]          // argument beats environment
    [InlineData(new[] { "--lang=de" }, null, "ru-RU")]       // no German resources → default
    [InlineData(new[] { "--lang=zz-!!" }, null, "ru-RU")]    // invalid tag → default
    [InlineData(new[] { "--lang" }, null, "ru-RU")]          // dangling flag → default
    [InlineData(new string[0], "", "ru-RU")]
    [InlineData(new string[0], null, "ru-RU")]
    public void Resolve_PicksArgumentThenEnvironmentThenDefault(string[] args, string? env, string expected)
    {
        Assert.Equal(expected, LocalizationManager.Resolve(args, env).Name);
    }

    [Fact]
    public void Resolve_NullArgs_UsesEnvironment()
    {
        Assert.Equal("en", LocalizationManager.Resolve(null, "en").Name);
    }

    [Theory]
    [InlineData("ru", true)]
    [InlineData("ru-RU", true)]
    [InlineData("en", true)]
    [InlineData("en-GB", true)]   // regional variant served by the neutral "en" satellite
    [InlineData("de", false)]
    [InlineData("ja-JP", false)]
    public void IsAvailable_ReflectsShippedResources(string culture, bool expected)
    {
        Assert.Equal(expected, LocalizationManager.IsAvailable(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void IsAvailable_IsNotFooledByResourceManagerFallbackCache()
    {
        // Regression: a fallback lookup under de-DE makes ResourceManager cache the
        // neutral (Russian) set under "de"; availability must not depend on that.
        using (new CultureScope("de-DE")) _ = Strings.Nav_System;
        Assert.False(LocalizationManager.IsAvailable(CultureInfo.GetCultureInfo("de")));
        Assert.Equal("ru-RU", LocalizationManager.Resolve(new[] { "--lang=de" }, null).Name);
    }

    [Fact]
    public void Invariant_IsNotALanguage()
    {
        Assert.False(LocalizationManager.IsAvailable(CultureInfo.InvariantCulture));
    }
}

/// <summary>Apply() changes process-wide defaults, so it runs isolated from parallel tests.</summary>
[CollectionDefinition(nameof(ProcessCultureCollection), DisableParallelization = true)]
public class ProcessCultureCollection { }

[Collection(nameof(ProcessCultureCollection))]
public class LocalizationManagerApplyTests
{
    [Fact]
    public async Task Apply_SetsCurrentAndDefaultThreadCultures()
    {
        var (c, ui, dc, dui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture,
                                CultureInfo.DefaultThreadCurrentCulture, CultureInfo.DefaultThreadCurrentUICulture);
        try
        {
            LocalizationManager.Apply(CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("System", Strings.Nav_System);
            // New threads (e.g. background work) inherit it too.
            var onNewThread = await Task.Factory.StartNew(() => CultureInfo.CurrentUICulture.Name,
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.Equal("en-US", onNewThread);
        }
        finally
        {
            CultureInfo.CurrentCulture = c;
            CultureInfo.CurrentUICulture = ui;
            CultureInfo.DefaultThreadCurrentCulture = dc;
            CultureInfo.DefaultThreadCurrentUICulture = dui;
        }
    }
}
