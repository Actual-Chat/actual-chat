using Microsoft.Maui.Platform;
using WinRT.Interop;
using Window = Microsoft.UI.Xaml.Window;

namespace ActualChat.App.Maui;

internal static partial class WindowConfigurator
{
    public static void Configure(Window window)
    {
        ConfigureMinimization(window);
        ConfigureStartupSize(window);
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

    private static void ConfigureMinimization(Window window)
    {
        WinUI.App.AppInstanceActivated += arguments => {
            window.DispatcherQueue.TryEnqueue(() => {
                if (arguments.Contains(JumpListManager.QuitArgs)) {
                    App.Current.Quit();
                    return;
                }

                try {
                    if (window.GetAppWindow() is { Presenter: Microsoft.UI.Windowing.OverlappedPresenter presenter }
                        && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
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
                if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) {
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
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                presenter.Maximize();
        }
        catch {
            // In unpackaged/AOT mode, GetAppWindow may fail — just activate the window
            window.Activate();
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);
}
