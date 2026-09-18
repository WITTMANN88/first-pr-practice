namespace Observation.Handlers.Diag;

/// <summary>Информация о драйвере одного видеоадаптера (вкладка «Диагностика», поиск устаревших драйверов — только чтение, без автоустановки).</summary>
public sealed record GpuDriverInfo(string Name, string DriverVersion, string DriverDate, string Vendor);
