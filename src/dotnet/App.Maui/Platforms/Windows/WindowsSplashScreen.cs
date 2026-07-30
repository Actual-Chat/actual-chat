using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using ActualChat.App.Maui.Services;
using Color = Microsoft.Maui.Graphics.Color;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using Image = Microsoft.UI.Xaml.Controls.Image;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using Stretch = Microsoft.UI.Xaml.Media.Stretch;
using Window = Microsoft.UI.Xaml.Window;

namespace ActualChat.App.Maui;

/// <summary>
/// A borderless always-on-top window covering the screen while the app starts. Windows has no splash
/// screen for unpackaged apps, and MAUI paints a white then a gray frame before Blazor content shows up
/// (dotnet/maui#19942) - covering the main window until it's rendered is the only way to not see them.
/// </summary>
public static class WindowsSplashScreen
{
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(200);
    private static readonly Color DefaultBackgroundColor = Color.FromArgb("#28282E");
    private static readonly double LogoWidth = 480;
    private static readonly TimeSpan ShowTimeout = TimeSpan.FromSeconds(1);
    private static readonly int ShowFrameCount = 3;
    private static Window? _splashWindow;
    private static Image? _logo;
    private static bool _isReady;

    public static void Show(Action whenShown)
    {
        // Called before MAUI creates its own window, so nothing of it is ever painted uncovered.
        // Building MauiApp blocks the UI thread for a while, so whenShown - which starts it - runs only
        // once the splash has actually rendered; otherwise the splash window stays black until that's done.
        try {
            _splashWindow = CreateWindow();
            _splashWindow.Activate();
            WhenShown(_splashWindow, whenShown);
        }
        catch (Exception e) {
            StaticLog.For(typeof(WindowsSplashScreen)).LogWarning(e, "Failed to show the splash window");
            _splashWindow = null;
            whenShown();
        }
    }

    public static void Attach(Window mainWindow)
    {
        if (_splashWindow == null)
            return;

        // The main window is deliberately left visible: WebView2 suspends rendering while hidden, so it
        // would repaint from scratch when revealed. The always-on-top splash hides it instead.
        mainWindow.Activated += OnMainWindowActivated;
        _ = RevealWhenReady(mainWindow);
    }

    // Private methods

    private static void OnMainWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs e)
    {
        if (!_isReady)
            _splashWindow?.Activate();
    }

    private static async Task RevealWhenReady(Window mainWindow)
    {
        // Capped: a failed load must not leave the app permanently covered.
        await MauiLoadingUI.WhenFirstSplashRemoved
            .WaitAsync(MaxDuration)
            .SilentAwait(false);
        mainWindow.DispatcherQueue.TryEnqueue(() => {
            _isReady = true;
            mainWindow.Activated -= OnMainWindowActivated;
            // Once while the splash still owns the foreground, so the main window is raised under it,
            // and once after it's gone - closing a topmost window otherwise hands the foreground to
            // whatever is next in z-order, which is some other app's window.
            WindowConfigurator.BringToForeground(mainWindow);
            FadeOutLogo(Close);
        });

        void Close()
        {
            _splashWindow?.Close();
            _splashWindow = null;
            _logo = null;
            WindowConfigurator.BringToForeground(mainWindow);
        }
    }

    private static void FadeOutLogo(Action whenDone)
    {
        // Only the logo is faded, not the window: the splash background is the theme background the
        // app is about to paint, so the logo is the single thing that actually changes on handover.
        if (_logo == null) {
            whenDone();
            return;
        }

        var fade = new DoubleAnimation {
            To = 0,
            Duration = new Microsoft.UI.Xaml.Duration(FadeDuration),
        };
        Storyboard.SetTarget(fade, _logo);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Completed += (_, _) => whenDone();
        storyboard.Begin();
    }

    private static void WhenShown(Window window, Action whenShown)
    {
        // The timer is a backstop: whenShown starts the app, so a render tick that never comes must
        // not block it forever.
        var isDone = false;
        var frameCount = 0;
        var timer = window.DispatcherQueue.CreateTimer();
        timer.Interval = ShowTimeout;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Done();
        timer.Start();
        CompositionTarget.Rendering += OnRendering;
        return;

        void OnRendering(object? sender, object e)
        {
            // Rendering fires as a frame is composed, before it's presented - so a single tick doesn't
            // mean the splash is on screen yet, and blocking the UI thread right after it would keep
            // the window black. A few ticks in mean earlier frames have made it out.
            if (++frameCount >= ShowFrameCount)
                Done();
        }

        void Done() {
            if (isDone)
                return;

            isDone = true;
            timer.Stop();
            CompositionTarget.Rendering -= OnRendering;
            whenShown();
        }
    }

    private static Window CreateWindow()
    {
        var background = GetBackgroundColor();
        _logo = CreateLogo(IsDark(background));
        var window = new Window {
            Content = new Grid {
                Background = new SolidColorBrush(background.ToWindowsColor()),
                Children = { _logo },
            },
        };
        window.ExtendsContentIntoTitleBar = true;
        var appWindow = window.AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter) {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.Maximize();
        }
        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        return window;
    }

    private static Color GetBackgroundColor()
    {
        // The theme background, so the splash, the WebView and the app all show the same color and the
        // transition is invisible. There's no stored theme on a first run - fall back to the dark theme's
        // --background-01 rather than the splash color, which blinks against every theme.
        var color = MauiThemeHandler.Instance.TopBarColor;
        return color.IsNullOrEmpty() ? DefaultBackgroundColor : Color.FromArgb(color);
    }

    private static bool IsDark(Color color)
    {
        var darkness = 1 - (0.299 * color.Red + 0.587 * color.Green + 0.114 * color.Blue);
        return darkness >= 0.5;
    }

    private static Image CreateLogo(bool isDark)
    {
        // Two variants of the same transparent artwork - a white and a black logo - so it stays visible
        // whichever theme background is behind it. splashscreen_voxtSplashScreen.* can't be used here:
        // it has the splash color baked in as an opaque rectangle.
        var name = isDark ? "splashlogo_light.png" : "splashlogo_dark.png";
        var path = Path.Combine(AppContext.BaseDirectory, "Platforms", "Windows", "Assets", name);
        return new Image {
            Source = new BitmapImage(new Uri(path)),
            Stretch = Stretch.Uniform,
            Width = LogoWidth,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
        };
    }
}
