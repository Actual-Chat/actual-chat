using ActualChat.UI.Blazor.App.Pages.Test;

namespace ActualChat.App.Maui.Services;

public class MauiTestPageBackend : MauiTestPage.IMauiTestPageBackend
{
    public void SimulateAppCrash()
        => _ = Task.Run(() => {
            MainThread.BeginInvokeOnMainThread(
                () => throw StandardError.Internal("Simulated application crash!"));
        });
}
