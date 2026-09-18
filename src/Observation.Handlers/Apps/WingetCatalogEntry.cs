namespace Observation.Handlers.Apps;

/// <summary>
/// Один пункт каталога установки (вкладка 8, «Каталог по категориям» в плане).
/// PackageId — реальный winget-идентификатор; список ниже (WingetCatalog.BuiltIn) —
/// широко известные, стабильные ID из официального winget-репозитория (не проверены
/// live-запросом winget search в этой песочнице — там нет Windows/winget вообще, только
/// на реальной машине можно подтвердить их окончательно).
/// </summary>
public sealed record WingetCatalogEntry(string Category, string Name, string PackageId);
