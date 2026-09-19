using Observation.Handlers.Debloat;

namespace Observation.App.ViewModels;

/// <summary>Обёртка одного отсканированного UWP-пакета — список динамический (реальный скан машины), не строчка из TweakLibrary.</summary>
public sealed class UwpPackageViewModel : ViewModelBase
{
    public UwpPackageInfo Info { get; }
    public string Name => Info.Name;
    public string Publisher => Info.Publisher;

    private bool _isRemoving;
    public bool IsRemoving
    {
        get => _isRemoving;
        set => SetField(ref _isRemoving, value);
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public UwpPackageViewModel(UwpPackageInfo info) => Info = info;
}
