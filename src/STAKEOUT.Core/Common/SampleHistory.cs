namespace Stakeout.Core;

/// <summary>
/// Fixed-capacity ring buffer of numeric samples (e.g. CPU temperature polled
/// every few seconds), plus the mapping to plot coordinates for a sparkline.
/// Pure logic with no UI types, so it is unit-tested; not thread-safe (fed from
/// the UI thread's timer).
/// </summary>
public sealed class SampleHistory
{
    private readonly double[] _buffer;
    private int _start;
    private int _count;

    public SampleHistory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        _buffer = new double[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count => _count;

    public double? Latest => _count == 0 ? null : At(_count - 1);
    public double? Min => _count == 0 ? null : Values().Min();
    public double? Max => _count == 0 ? null : Values().Max();

    /// <summary>Append a sample; the oldest is dropped when full. NaN/∞ are ignored.</summary>
    public void Add(double value)
    {
        if (!double.IsFinite(value)) return;
        if (_count < _buffer.Length)
        {
            _buffer[(_start + _count) % _buffer.Length] = value;
            _count++;
        }
        else
        {
            _buffer[_start] = value;
            _start = (_start + 1) % _buffer.Length;
        }
    }

    public void Clear() => _start = _count = 0;

    /// <summary>Samples from oldest to newest.</summary>
    public IReadOnlyList<double> ToArray() => Values().ToArray();

    /// <summary>
    /// Vertical domain: [<paramref name="floor"/>, <paramref name="ceiling"/>], widened to
    /// include every sample. A fixed base keeps a reference line (e.g. 85 °C) always
    /// visible and makes the height meaningful; widening means nothing is clipped.
    /// </summary>
    public (double Min, double Max) Domain(double floor, double ceiling)
    {
        if (ceiling <= floor) throw new ArgumentException("ceiling must be greater than floor.");
        return _count == 0 ? (floor, ceiling) : (Math.Min(floor, Min!.Value), Math.Max(ceiling, Max!.Value));
    }

    /// <summary>
    /// Plot coordinates (Y grows downward), right-aligned: the newest sample sits at
    /// x = <paramref name="width"/> and earlier ones step left by width / (Capacity − 1),
    /// so the line "enters from the right" as history fills up.
    /// </summary>
    public IReadOnlyList<(double X, double Y)> ToPoints(double width, double height, double domainMin, double domainMax)
    {
        var step = width / (_buffer.Length - 1);
        var points = new (double X, double Y)[_count];
        for (var i = 0; i < _count; i++)
            points[i] = (width - (_count - 1 - i) * step, ScaleY(At(i), height, domainMin, domainMax));
        return points;
    }

    /// <summary>Map a value to Y in [0, height] (top = domainMax), clamped.</summary>
    public static double ScaleY(double value, double height, double domainMin, double domainMax)
    {
        if (domainMax <= domainMin) return height / 2;
        var t = Math.Clamp((value - domainMin) / (domainMax - domainMin), 0, 1);
        return height - t * height;
    }

    private double At(int i) => _buffer[(_start + i) % _buffer.Length];

    private IEnumerable<double> Values()
    {
        for (var i = 0; i < _count; i++) yield return At(i);
    }
}
