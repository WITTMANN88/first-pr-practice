using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Stakeout.ViewModels;

namespace Stakeout;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        Loaded += OnLoaded;
        Closed += (_, _) => _vm.Shutdown();
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Kick off the first data load in the background.
        _ = _vm.InitializeAsync();

        // Reveal sequence: hold the skull briefly, then fade the splash out while
        // the interface "unfolds" around it (fade + slight scale-up).
        await Task.Delay(700);
        PlayReveal();
    }

    private void PlayReveal()
    {
        var fadeSplash = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600));
        fadeSplash.Completed += (_, _) => Splash.Visibility = Visibility.Collapsed;
        Splash.BeginAnimation(OpacityProperty, fadeSplash);

        Shell.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)) { BeginTime = TimeSpan.FromMilliseconds(200) });

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var scale = new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(700))
        { BeginTime = TimeSpan.FromMilliseconds(200), EasingFunction = ease };
        ShellScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        ShellScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale.Clone());
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSidebarCollapsed))
            AnimateSidebar(_vm.IsSidebarCollapsed);
    }

    /// <summary>Collapse/expand the sidebar to icons over 300 ms (cubic ease).</summary>
    private void AnimateSidebar(bool collapsed)
    {
        var target = collapsed ? 64d : 220d;
        var anim = new DoubleAnimation(Sidebar.ActualWidth, target, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Sidebar.BeginAnimation(WidthProperty, anim);
    }

    // --- caption buttons ---
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // --- hidden "revert all": double-click only ---
    private void OnRevertAllClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && _vm.RevertAllCommand.CanExecute(null))
            _vm.RevertAllCommand.Execute(null);
    }
}
