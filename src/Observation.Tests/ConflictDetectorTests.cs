using Observation.Core.Conflicts;
using Xunit;

namespace Observation.Tests;

public class ConflictDetectorTests
{
    [Fact]
    public void Scan_FindsKnownContradictoryPair_RegardlessOfOrder()
    {
        var detector = new ConflictDetector();

        var found = detector.Scan(new[] { "security.cfa.enable", "security.defender.disable", "unrelated.tweak" });

        Assert.Contains(found, r => r.Severity == ConflictSeverity.Contradictory
            && r.TweakIdA == "security.defender.disable" && r.TweakIdB == "security.cfa.enable");
    }

    [Fact]
    public void Scan_FindsRedundantPair()
    {
        var detector = new ConflictDetector();

        var found = detector.Scan(new[] { "updates.defer", "updates.disable" });

        Assert.Contains(found, r => r.Severity == ConflictSeverity.Redundant);
    }

    [Fact]
    public void Scan_ReturnsEmpty_WhenNoConflictingPairSelected()
    {
        var detector = new ConflictDetector();

        var found = detector.Scan(new[] { "perf.gamemode", "apps.discord.hwaccel" });

        Assert.Empty(found);
    }

    [Fact]
    public void Scan_DoesNotMatch_WhenOnlyOneSideOfPairPresent()
    {
        var detector = new ConflictDetector();

        var found = detector.Scan(new[] { "security.defender.disable" });

        Assert.Empty(found);
    }
}
