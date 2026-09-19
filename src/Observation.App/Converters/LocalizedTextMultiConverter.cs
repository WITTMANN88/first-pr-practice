using System.Globalization;
using System.Windows.Data;
using Observation.App.Services;

namespace Observation.App.Converters;

/// <summary>
/// Для динамического ключа (например, у пунктов сайдбара, где ключ приходит из NavItem
/// через биндинг, а не задан статически в XAML). Первое значение — ключ, второе —
/// ILocalizationService.CurrentLanguage как триггер пересчёта при смене языка.
/// </summary>
public sealed class LocalizedTextMultiConverter : IMultiValueConverter
{
    private readonly ILocalizationService _localization;

    public LocalizedTextMultiConverter(ILocalizationService localization) => _localization = localization;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is string key ? _localization[key] : string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
