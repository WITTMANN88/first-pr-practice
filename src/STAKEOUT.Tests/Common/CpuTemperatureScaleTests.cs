using Stakeout.Core;

namespace Stakeout.Tests.Common;

public class CpuTemperatureScaleTests
{
    [Fact]
    public void HistoryWindow_IsTheTwoMinutesTheCaptionPromises()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), CpuTemperatureScale.Window);
    }

    [Fact]
    public void ReferenceLine_IsInsideTheBaseDomain()
    {
        Assert.InRange(CpuTemperatureScale.HotThresholdC,
            CpuTemperatureScale.DomainFloorC, CpuTemperatureScale.DomainCeilingC);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(84.9, false)]
    [InlineData(85.0, false)]   // "exceeds 85 °C" means strictly above
    [InlineData(85.1, true)]
    [InlineData(97.0, true)]
    public void IsHot_IsStrictlyAboveTheThreshold(double? celsius, bool hot)
    {
        Assert.Equal(hot, CpuTemperatureScale.IsHot(celsius));
    }
}
