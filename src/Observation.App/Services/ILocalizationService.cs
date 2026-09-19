using System.ComponentModel;

namespace Observation.App.Services;

/// <summary>
/// Локализация без перезапуска — сервис со словарями RU/EN, привязанный в XAML через
/// индексатор (см. «Хранение данных, окно, локализация» в плане). Переключение языка
/// поднимает PropertyChanged("Item[]"), что заставляет WPF перечитать все Binding
/// Path="[key]" сразу, без пересоздания окна.
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    string CurrentLanguage { get; }
    string this[string key] { get; }
    void SetLanguage(string languageCode);
}
