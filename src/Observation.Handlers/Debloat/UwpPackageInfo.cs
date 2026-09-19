namespace Observation.Handlers.Debloat;

/// <summary>Один установленный UWP-пакет из сканирования (вкладка 5, «Сканирование UWP-приложений» в плане).</summary>
public sealed record UwpPackageInfo(string PackageFullName, string Name, string Publisher);
