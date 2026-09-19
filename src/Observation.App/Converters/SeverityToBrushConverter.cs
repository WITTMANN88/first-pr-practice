using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Observation.Core.Tweaks;

namespace Observation.App.Converters;

/// <summary>Severity → цвет чипа (safe/situational/risky → Ok/Warn/Danger), как в макете (.chip.safe и т.п.).</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is Severity severity
            ? severity switch
            {
                Severity.Safe => "Color.Ok",
                Severity.Situational => "Color.Warn",
                _ => "Color.Danger"
            }
            : "Color.TextMuted";

        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
