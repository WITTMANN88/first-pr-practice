namespace Stakeout.Core;

/// <summary>Why no CPU temperature could be read (shown instead of the chart, and logged).</summary>
public enum CpuTemperatureGap
{
    /// <summary>A reading was taken.</summary>
    None,
    /// <summary>
    /// The sensor library (LibreHardwareMonitor 0.9.5+) reads CPU registers only
    /// through the PawnIO kernel driver, and it is not installed.
    /// </summary>
    DriverMissing,
    /// <summary>The driver is installed, but the CPU exposes no temperature sensor.</summary>
    NoSensor,
    /// <summary>Reading the sensors failed; the log has the exception.</summary>
    Error,
}
