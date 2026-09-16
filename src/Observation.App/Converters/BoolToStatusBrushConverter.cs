using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Observation.App.Converters;

/// <summary>Success (true/false) → цвет (Ok/Danger) — для строк панели журнала на «Главной».</summary>
public sealed class BoolToStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is true ? "Color.Ok" : "Color.Danger";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
