using Observation.App.Services;
using Observation.Handlers.Diag;

namespace Observation.App.ViewModels;

/// <summary>Обёртка одного проблемного устройства из скана — «искать в интернете» по имени устройства и коду ошибки.</summary>
public sealed class ProblemDeviceViewModel
{
    public ProblemDeviceInfo Info { get; }
    public string Name => Info.Name;
    public string ErrorLabel => $"Code {Info.ErrorCode}";

    public RelayCommand SearchCommand { get; }

    public ProblemDeviceViewModel(ProblemDeviceInfo info)
    {
        Info = info;
        SearchCommand = new RelayCommand(() => ShellLauncher.SearchOnline($"{Info.Name} error code {Info.ErrorCode} windows"));
    }
}
