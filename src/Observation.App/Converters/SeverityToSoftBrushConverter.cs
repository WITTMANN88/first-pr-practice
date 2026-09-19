using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Observation.Core.Tweaks;

namespace Observation.App.Converters;

/// <summary>Severity → смягчённая (16% альфа) заливка чипа важности — см. Color.*Soft в Colors.xaml.</summary>
public sealed class SeverityToSoftBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is Severity severity
            ? severity switch
            {
                Severity.Safe => "Color.OkSoft",
                Severity.Situational => "Color.WarnSoft",
                _ => "Color.DangerSoft"
            }
            : "Color.Border";

        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
