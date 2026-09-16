using Observation.App.Services;

namespace Observation.App.ViewModels;

/// <summary>
/// Заглушка вкладки «Главная» (пресеты/сводка/применение) — по договорённости этот
/// архитектурный проход не включает пиксель-точную стилизацию содержимого вкладок,
/// только каркас (окно/сайдбар/навигация/локализация/генератор списка твиков).
/// </summary>
public sealed class HomeTabViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }

    public HomeTabViewModel(ILocalizationService localization) => Localization = localization;
}
