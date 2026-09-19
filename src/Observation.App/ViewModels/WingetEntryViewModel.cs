using Observation.Handlers.Apps;

namespace Observation.App.ViewModels;

public enum WingetInstallState
{
    Idle,
    Installing,
    Installed,
    Failed
}

/// <summary>Обёртка одного пункта каталога winget — состояние живёт только в интерфейсе, не в TweakLibrary (установка — разовое действие, не тумблер).</summary>
public sealed class WingetEntryViewModel : ViewModelBase
{
    public WingetCatalogEntry Entry { get; }
    public string Name => Entry.Name;
    public string Category => Entry.Category;

    private WingetInstallState _state = WingetInstallState.Idle;
    public WingetInstallState State
    {
        get => _state;
        set => SetField(ref _state, value);
    }

    private string? _message;
    public string? Message
    {
        get => _message;
        set => SetField(ref _message, value);
    }

    public WingetEntryViewModel(WingetCatalogEntry entry) => Entry = entry;
}
