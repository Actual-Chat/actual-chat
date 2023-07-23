using Microsoft.Maui.Platform;

namespace ActualChat.App.Maui;

internal static class WindowsMinimization
{
    public static void Configure(Microsoft.UI.Xaml.Window window)
    {
        var mustMinimizeOnClose = true;
        WinUI.App.AppInstanceActivated += arguments => {
            if (arguments.Contains(JumpListManager.QuitArgs)) {
                mustMinimizeOnClose = false;
                window.DispatcherQueue.TryEnqueue(window.Close);
            }
            else {
                window.DispatcherQueue.TryEnqueue(window.Activate);
            }
        };

        _ = JumpListManager.PopulateJumpList();
        window.Closed += (_, _) => {
            var t = Task.Run(JumpListManager.ClearJumpList);
#pragma warning disable VSTHRD002
            _ = t.Wait(TimeSpan.FromSeconds(5));
#pragma warning restore VSTHRD002
        };

        var appWindow = window.GetAppWindow()!;
        appWindow.Closing += (s, e) => {
            if (mustMinimizeOnClose && !App.MustQuit) {
                var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)appWindow.Presenter;
                presenter.Minimize();
                e.Cancel = true;
            }
        };
    }
}
