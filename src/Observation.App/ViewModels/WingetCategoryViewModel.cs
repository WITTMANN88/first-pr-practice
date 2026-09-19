namespace Observation.App.ViewModels;

/// <summary>Группа каталога winget по категории (Браузеры/Разработка/Игры/... — см. план, «Каталог по категориям»).</summary>
public sealed class WingetCategoryViewModel
{
    public string Name { get; }
    public IReadOnlyList<WingetEntryViewModel> Entries { get; }

    public WingetCategoryViewModel(string name, IReadOnlyList<WingetEntryViewModel> entries)
    {
        Name = name;
        Entries = entries;
    }
}
