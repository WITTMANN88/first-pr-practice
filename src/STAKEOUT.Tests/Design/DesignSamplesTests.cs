using Stakeout.Core;
using Stakeout.Design;
using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.Tests.Design;

/// <summary>
/// The XAML designer only shows what these samples contain, so pin the
/// properties it is meant to demonstrate. Also catches catalogue changes that
/// would silently re-badge a sample.
/// </summary>
public class DesignSamplesTests
{
    [Fact]
    public void Apps_ShowEveryCategoryBadge()
    {
        var shown = DesignSamples.Apps().Select(a => a.Category).ToHashSet();
        Assert.Equal(Enum.GetValues<AppCategory>().ToHashSet(), shown);
    }

    [Fact]
    public void Apps_IncludeBothKinds_ThroughTheRealFilter()
    {
        var apps = DesignSamples.Apps();
        Assert.Contains(apps, a => a.Kind == Stakeout.Models.AppKind.Uwp);
        Assert.Contains(apps, a => a.Kind == Stakeout.Models.AppKind.Desktop && a.IsPerUser);
        // The KB update in the samples is filtered out, as on a real machine.
        Assert.DoesNotContain(apps, a => a.DisplayName.Contains("KB5005565", StringComparison.Ordinal));
    }

    [Fact]
    public void Apps_IncludeProtectedAndRemovable_WithSizes_AndUniqueNames()
    {
        var apps = DesignSamples.Apps();
        Assert.Contains(apps, a => a.IsCritical);
        Assert.Contains(apps, a => !a.IsCritical && a.Category == AppCategory.Bloatware);
        Assert.All(apps, a => Assert.True(a.SizeBytes > 0, a.Name));
        Assert.All(apps.Where(a => a.Kind == Stakeout.Models.AppKind.Uwp),
            a => Assert.StartsWith(a.Name + "_", a.PackageFullName, StringComparison.Ordinal));
        Assert.Equal(apps.Count, apps.Select(a => a.Name).Distinct().Count());
    }

    [Fact]
    public void UwpApps_ProtectedPackagesAreBadgedSystem()
    {
        Assert.All(DesignSamples.Apps().Where(a => a.IsCritical),
            a => Assert.Equal(AppCategory.System, a.Category));
    }

    [Fact]
    public void LogLines_AreInTheRealFormat_AndCoverEveryLevel()
    {
        var entries = DesignSamples.LogLines().Select(LogEntry.Parse).ToList();

        Assert.All(entries, e =>
        {
            Assert.Matches(@"^\d\d:\d\d:\d\d$", e.Time);   // parsed, not kept verbatim
            Assert.NotEmpty(e.Action);
            Assert.NotEmpty(e.Status);
        });
        Assert.Equal(Enum.GetValues<LogLevel>().ToHashSet(), entries.Select(e => e.Level).ToHashSet());
    }

    [Fact]
    public void LogLines_SurviveTheEncryptedFileRoundTrip()
    {
        using var dir = new TestSupport.TempDir();
        var file = new EncryptedLogFile(dir.File("design.log"), LogCipher.ForMachine("DESIGN-PC"));
        foreach (var line in DesignSamples.LogLines()) file.Append(line);

        Assert.Equal(DesignSamples.LogLines(), file.ReadAll());
    }

    [Fact]
    public void CpuTemperatures_FillTheWindow_AndCrossTheOverheatLine()
    {
        var temps = DesignSamples.CpuTemperatures();
        Assert.Equal(CpuTemperatureScale.HistoryLength, temps.Count);
        Assert.Contains(temps, t => CpuTemperatureScale.IsHot(t));
        Assert.False(CpuTemperatureScale.IsHot(temps[^1]));   // ends cooled down: the bar is not red
        Assert.All(temps, t => Assert.InRange(t, CpuTemperatureScale.DomainFloorC, CpuTemperatureScale.DomainCeilingC));
    }
}
