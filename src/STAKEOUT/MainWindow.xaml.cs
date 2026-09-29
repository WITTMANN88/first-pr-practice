using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Stakeout.Core;
using Stakeout.Infrastructure;
using Stakeout.Localization;
using Stakeout.ViewModels;

namespace Stakeout;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    /// <summary>Element that had focus before the log modal opened (restored on close).</summary>
    private IInputElement? _focusBeforeLogs;

    /// <summary>Created by the DI container (see Infrastructure/ServiceRegistration).</summary>
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        LoadSplashImage();
        MaximizedWindowHook.Attach(this);
        _vm = vm;
        DataContext = _vm;

        Loaded += OnLoaded;
        Closed += OnClosed;
        StateChanged += OnStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;

        // The view models are container singletons and outlive any window, so
        // subscribe weakly: they must never keep a closed window (and its visual
        // tree) alive. Filtered by property name, so unrelated changes cost nothing.
        PropertyChangedEventManager.AddHandler(_vm, OnVmPropertyChanged, nameof(MainViewModel.IsSidebarCollapsed));
        PropertyChangedEventManager.AddHandler(_vm.Logs, OnLogsPropertyChanged, nameof(LogViewerViewModel.IsOpen));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        PropertyChangedEventManager.RemoveHandler(_vm, OnVmPropertyChanged, nameof(MainViewModel.IsSidebarCollapsed));
        PropertyChangedEventManager.RemoveHandler(_vm.Logs, OnLogsPropertyChanged, nameof(LogViewerViewModel.IsOpen));
        _vm.Shutdown();
    }

    /// <summary>The splash emblem, compiled into the exe as a WPF Resource (see STAKEOUT.csproj).</summary>
    private static readonly Uri SplashImageUri = new("pack://application:,,,/Assets/CustomSplash.png", UriKind.Absolute);

    private static readonly TimeSpan SplashFadeIn = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan SplashHold = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// Decode the emblem once (OnLoad), frozen. The asset is stored at 1300 px,
    /// the width it is shown at (520 DIP) on a 250 % display: PNG cannot be
    /// decoded at a reduced size, so a larger source would only cost startup
    /// time and memory. A build without the file shows the name only instead
    /// of failing.
    /// </summary>
    private void LoadSplashImage()
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = SplashImageUri;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            SplashImage.Source = image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or InvalidOperationException)
        {
            SplashImage.Visibility = Visibility.Collapsed;
            Logger.Log("Splash", "WARNING", "Assets/CustomSplash.png is not in this build: " + ex.Message);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Kick off the first data load in the background.
        _ = _vm.InitializeAsync();

        // Splash: the emblem fades in from transparent, holds, then fades out while
        // the interface "unfolds" around it (fade + slight scale-up).
        SplashContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, SplashFadeIn)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        await Task.Delay(SplashFadeIn + SplashHold);
        PlayReveal();
        _vm.AnnounceStartupState();
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

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => AnimateSidebar(_vm.IsSidebarCollapsed);

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

    // --- log viewer modal ---

    private void OnLogsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => AnimateLogOverlay(_vm.Logs.IsOpen);

    /// <summary>
    /// Open: backdrop fades in (180 ms) while the panel scales 0.96→1 and rises
    /// 12px (240 ms, cubic ease-out). Close: the reverse, faster (150 ms), then
    /// the overlay is collapsed so it costs nothing while hidden.
    /// All animations are "To"-only, so a close during an open (or vice versa)
    /// continues smoothly from the current value instead of jumping.
    /// </summary>
    private void AnimateLogOverlay(bool open)
    {
        if (open)
        {
            _focusBeforeLogs = Keyboard.FocusedElement;
            LogOverlay.Visibility = Visibility.Visible;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var grow = new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };
            LogOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            LogPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            LogPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            LogPanelShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });

            // Move focus into the modal once it is visible, so Tab cycles inside it.
            Dispatcher.BeginInvoke(() => LogView.FocusDefault(), DispatcherPriority.Input);
        }
        else
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var shrink = new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (_, _) =>
            {
                // Guard: the modal may have been reopened while fading out.
                if (!_vm.Logs.IsOpen) LogOverlay.Visibility = Visibility.Collapsed;
            };
            LogOverlay.BeginAnimation(OpacityProperty, fade);
            LogPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            LogPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            LogPanelShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(12, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });

            _focusBeforeLogs?.Focus();
            _focusBeforeLogs = null;
        }
    }

    /// <summary>Esc closes the log modal.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm.Logs.IsOpen)
        {
            _vm.Logs.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Click on the dimmed backdrop (outside the panel) closes the modal.</summary>
    private void OnLogBackdropClick(object sender, MouseButtonEventArgs e)
    {
        _vm.Logs.CloseCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// While maximized the middle caption button restores: its glyph, tooltip
    /// and accessible name say so (Windows' own caption buttons do the same).
    /// </summary>
    private void OnStateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        var label = maximized ? Strings.Caption_Restore : Strings.Caption_Maximize;
        MaxBtn.Content = maximized ? "❐" : "▢";
        MaxBtn.ToolTip = label;
        AutomationProperties.SetName(MaxBtn, label);
    }

    // --- caption buttons ---
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // --- hidden "revert all": double-click only (ClickCount is reliable on button-down) ---
    private void OnRevertAllClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        e.Handled = true;
        if (_vm.RevertAllCommand.CanExecute(null))
            _vm.RevertAllCommand.Execute(null);
    }
}
