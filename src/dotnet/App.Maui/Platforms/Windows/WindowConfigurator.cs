using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Color = Microsoft.Maui.Graphics.Color;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using Window = Microsoft.UI.Xaml.Window;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace ActualChat.App.Maui;

/// <summary>
/// Desktop window policy of the Windows app: the close button minimizes while
/// <see cref="App.MustMinimizeOnQuit"/> holds, and the title bar takes the theme's navbar and text colors.
/// </summary>
internal static partial class WindowConfigurator
{
    private static AppWindow? _appWindow;

    public static void Configure(Window window)
    {
        ConfigureMinimization(window);
        ConfigureStartupSize(window);
        ConfigureTitleBar(window);
        WindowsSplashScreen.Attach(window);
    }

    public static void BringToForeground(Window window)
    {
        window.Activate();
        try {
            // Activate() alone rarely wins the foreground lock - from the browser we're returning
            // from, or from whatever Windows picks when the splash window above us closes.
            SetForegroundWindow(WindowNative.GetWindowHandle(window));
        }
        catch {
            // GetWindowHandle may fail in unpackaged/AOT mode, same as GetAppWindow elsewhere here
        }
    }

    public static void UpdateTitleBar(string backgroundColor, string textColor)
    {
        if (backgroundColor.IsNullOrEmpty() || textColor.IsNullOrEmpty())
            return;

        var background = Color.FromArgb(backgroundColor);
        var text = Color.FromArgb(textColor);
        // The title strip is MauiAppTitleBarTemplate from App.xaml, painted with these brushes
        SetBrushColor("VoxtTitleBarBackgroundBrush", background);
        SetBrushColor("VoxtTitleBarForegroundBrush", text);
        if (_appWindow is not { } appWindow)
            return;

        // The caption buttons keep MAUI's transparent backgrounds over the strip and the same colors in an
        // inactive window. The title bar takes transparency only for those plain backgrounds, so the hover
        // and pressed shades are blended against the strip color.
        var foreground = text.ToWindowsColor();
        var titleBar = appWindow.TitleBar;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = Blend(background, text, 0.1f).ToWindowsColor();
        titleBar.ButtonPressedBackgroundColor = Blend(background, text, 0.2f).ToWindowsColor();
    }

    // Private methods

    private static void ConfigureMinimization(Window window)
    {
        WinUI.App.AppInstanceActivated += arguments => {
            window.DispatcherQueue.TryEnqueue(() => {
                if (arguments.Contains(JumpListManager.QuitArgs)) {
                    App.Current.Quit();
                    return;
                }

                try {
                    if (window.GetAppWindow() is { Presenter: OverlappedPresenter presenter }
                        && presenter.State == OverlappedPresenterState.Minimized)
                        presenter.Restore();
                }
                catch {
                    // In unpackaged/AOT mode, GetAppWindow may fail
                }
                BringToForeground(window);
            });
        };

        _ = JumpListManager.PopulateJumpList();
        window.Closed += (_, _) => {
            var t = Task.Run(JumpListManager.ClearJumpList);
            _ = t.Wait(TimeSpan.FromSeconds(5));
        };

        try {
            var appWindow = window.GetAppWindow()!;
            appWindow.Closing += (_, e) => {
                if (!App.MustMinimizeOnQuit)
                    return;

                // Presenter may not be OverlappedPresenter — e.g. CompactOverlay /
                // FullScreen. Use pattern-match to avoid InvalidCastException FCE.
                if (appWindow.Presenter is OverlappedPresenter presenter) {
                    presenter.Minimize();
                    e.Cancel = true;
                }
            };
        }
        catch {
            // In unpackaged/AOT mode, GetAppWindow may fail
        }
    }

    private static void ConfigureStartupSize(Window window)
    {
        try {
            var appWindow = window.GetAppWindow()!;
            if (appWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();
        }
        catch {
            // In unpackaged/AOT mode, GetAppWindow may fail — just activate the window
            window.Activate();
        }
    }

    private static void ConfigureTitleBar(Window window)
    {
        try {
            if (AppWindowTitleBar.IsCustomizationSupported())
                _appWindow = window.GetAppWindow();
        }
        catch {
            // In unpackaged/AOT mode, GetAppWindow may fail
        }
        // The web app reports its theme only once it's loaded, so the window starts with the stored one
        var colors = MauiThemeHandler.Instance.CurrentColors;
        UpdateTitleBar(colors.Navbar, colors.Text);
    }

    private static void SetBrushColor(string resourceKey, Color color)
    {
        if (XamlApplication.Current.Resources.TryGetValue(resourceKey, out var resource)
            && resource is SolidColorBrush brush)
            brush.Color = color.ToWindowsColor();
    }

    private static Color Blend(Color background, Color foreground, float amount)
        => new(
            background.Red + (foreground.Red - background.Red) * amount,
            background.Green + (foreground.Green - background.Green) * amount,
            background.Blue + (foreground.Blue - background.Blue) * amount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);
}
