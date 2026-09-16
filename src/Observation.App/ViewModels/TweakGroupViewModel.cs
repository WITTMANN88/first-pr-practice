namespace Observation.App.ViewModels;

/// <summary>Твики одной вкладки, сгруппированные по полю "group" JSON-реестра.</summary>
public sealed class TweakGroupViewModel
{
    public required string GroupName { get; init; }
    public required IReadOnlyList<TweakItemViewModel> Items { get; init; }
}
