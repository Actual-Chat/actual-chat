using Microsoft.Maui.Platform;
using Window = Microsoft.UI.Xaml.Window;

namespace ActualChat.App.Maui;

internal static class WindowConfigurator
{
    public static void Configure(Window window)
    {
        ConfigureMinimization(window);
        ConfigureStartupSize(window);
    }

    private static void ConfigureMinimization(Window window)
    {
        WinUI.App.AppInstanceActivated += arguments => {
            window.DispatcherQueue.TryEnqueue(() => {
                if (arguments.Contains(JumpListManager.QuitArgs))
                    App.Current.Quit();
                else
                    window.Activate();
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

                var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)appWindow.Presenter;
                presenter.Minimize();
                e.Cancel = true;
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
            var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)appWindow.Presenter;
            presenter.Maximize();
        }
        catch {
            // In unpackaged/AOT mode, GetAppWindow may fail — just activate the window
            window.Activate();
        }
    }
}
