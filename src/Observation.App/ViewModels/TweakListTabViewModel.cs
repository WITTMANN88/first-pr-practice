namespace Observation.App.ViewModels;

/// <summary>
/// Обёртка вкладки над группами, уже собранными TweakLibrary.GroupsForTab — сама
/// вкладка не хранит состояние, поэтому её можно пересоздавать при каждой навигации,
/// не теряя тумблеры (они живут в общих TweakItemViewModel внутри TweakLibrary).
/// </summary>
public sealed class TweakListTabViewModel : ViewModelBase
{
    public IReadOnlyList<TweakGroupViewModel> Groups { get; }
    public bool IsEmpty => Groups.Count == 0;

    public TweakListTabViewModel(IReadOnlyList<TweakGroupViewModel> groups) => Groups = groups;
}
