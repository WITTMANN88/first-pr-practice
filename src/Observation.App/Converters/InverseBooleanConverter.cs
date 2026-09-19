using System.Globalization;
using System.Windows.Data;

namespace Observation.App.Converters;

/// <summary>Инвертирует bool — например, чтобы задизейблить кнопку, пока идёт связанное действие (IsRemoving/IsScanning и т.п.).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
