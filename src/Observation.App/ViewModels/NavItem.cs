namespace Observation.App.ViewModels;

/// <summary>Один пункт сайдбара — id совпадает с полем "tab" в JSON-реестре твиков и с data-panel из ui-preview.html.</summary>
public sealed class NavItem
{
    public required string Id { get; init; }
    public required string LocalizationKey { get; init; }
}
