namespace Observation.Core.SystemAccess;

/// <summary>Карточка характеристик ПК на «Главной» — только для чтения (см. план, «Вкладка 1 — Главная»).</summary>
public sealed record PcSpecs(string OsCaption, string CpuName, string GpuName, string RamTotal, string DiskInfo);
