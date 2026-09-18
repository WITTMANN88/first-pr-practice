namespace Observation.Handlers.Diag;

/// <summary>
/// Текущая загрузка CPU обработкой DPC/прерываний в процентах (не микросекундная задержка —
/// см. PowerShellDpcActivitySampler, почему это честная замена, а не то же самое).
/// </summary>
public sealed record DpcActivitySample(double DpcPercent, double InterruptPercent);
