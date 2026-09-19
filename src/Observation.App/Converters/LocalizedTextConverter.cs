using System.Globalization;
using System.Windows.Data;
using Observation.App.Services;

namespace Observation.App.Converters;

/// <summary>
/// Для статического ключа, известного в XAML заранее (ConverterParameter). Привязывается
/// к ILocalizationService.CurrentLanguage только как триггеру пересчёта при смене языка —
/// сам текст берётся из инжектированного сервиса, а не из значения биндинга.
/// </summary>
public sealed class LocalizedTextConverter : IValueConverter
{
    private readonly ILocalizationService _localization;

    public LocalizedTextConverter(ILocalizationService localization) => _localization = localization;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is string key ? _localization[key] : string.Empty;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
