using ActualChat.UI.Blazor.App.Pages.Test;

namespace ActualChat.App.Maui.Services;

public class MauiTestPageBackend : MauiTestPage.IMauiTestPageBackend
{
    public void SimulateAppCrash()
        => BeginDispatchToMainThread(
            () => throw StandardError.Internal("Simulated application crash!"),
            allowInline: false);

    public void SimulateActivityDestroy()
    {
        #if ANDROID
        MainActivity.Current.Finish();
        #endif
    }
}
