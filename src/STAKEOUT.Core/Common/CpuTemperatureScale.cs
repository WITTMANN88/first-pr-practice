namespace Stakeout.Core;

/// <summary>
/// One source of truth for how CPU temperature is sampled and drawn: the live
/// bar's "hot" colour, the sparkline's reference line and vertical range, and
/// the history window the "~2 min" caption promises.
/// </summary>
public static class CpuTemperatureScale
{
    /// <summary>Above this (spec: "при превышении 85 °C") the bar turns red; the sparkline's reference line sits here.</summary>
    public const double HotThresholdC = 85;

    /// <summary>Fixed vertical base of the sparkline; widened only if a sample falls outside.</summary>
    public const double DomainFloorC = 30;
    public const double DomainCeilingC = 100;

    /// <summary>Re-poll interval of the (cheap) temperature sensor.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    /// <summary>Samples kept: 40 × 3 s = 2 minutes.</summary>
    public const int HistoryLength = 40;

    public static TimeSpan Window => PollInterval * HistoryLength;

    public static bool IsHot(double? celsius) => celsius > HotThresholdC;
}
