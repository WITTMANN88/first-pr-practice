using Observation.Core.SystemAccess;

namespace Observation.App.Services;

/// <summary>
/// Обёртка вокруг единоразового результата AntivirusSelfCheck (запускается один раз при
/// старте в App.xaml.cs) с флагом "закрыто пользователем" — сам SelfCheckResult иммутабелен,
/// а HomeTabViewModel пересоздаётся при каждом переходе на "Главную" (см. MainWindowViewModel.
/// CreateTab), поэтому состояние "баннер закрыт" нужно хранить отдельно, на уровне,
/// переживающем пересоздание вкладки.
/// </summary>
public sealed class AntivirusBannerState
{
    public SelfCheckResult Result { get; }
    public bool Dismissed { get; set; }

    public AntivirusBannerState(SelfCheckResult result)
    {
        Result = result;
    }

    public bool ShouldShow => !Result.IsHealthy && !Dismissed;
}
