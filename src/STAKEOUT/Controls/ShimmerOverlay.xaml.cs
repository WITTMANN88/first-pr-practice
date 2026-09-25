using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Stakeout.Controls;

/// <summary>
/// Animated shimmer band (see ShimmerOverlay.xaml). Layer it over any dark
/// placeholder or progress bar.
///
/// The sweep runs only while the control is actually visible: an infinite
/// animation on a collapsed element would otherwise keep WPF's render loop
/// ticking at 60 fps for nothing once loading has finished.
/// </summary>
public partial class ShimmerOverlay : UserControl
{
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(ShimmerOverlay),
            new PropertyMetadata(new CornerRadius(0)));

    /// <summary>Corner radius of the band, to match a rounded host (e.g. progress bar).</summary>
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private readonly Storyboard _sweep;

    public ShimmerOverlay()
    {
        InitializeComponent();
        _sweep = (Storyboard)Resources["Sweep"];
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            _sweep.Begin(this, isControllable: true);
        else
            _sweep.Stop(this);
    }
}
