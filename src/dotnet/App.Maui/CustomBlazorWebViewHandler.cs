using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

/// <summary>
/// Custom Blazor WebView handler that ensures the app services are ready before configuring the MAUI context.
/// </summary>
public sealed partial class CustomBlazorWebViewHandler : BlazorWebViewHandler
{
    private ILogger Log => field ??= StaticLog.For(GetType());

    public override void SetMauiContext(IMauiContext mauiContext)
    {
        BlazorWebViewApp.EnsureStarted();

        // MainPage attaches the WebView only once the app is ready, so this normally doesn't wait
        // at all; it still can via MainPage.MaxAttachDelay. Blocking rather than spinning matters
        // there - the old poll burned the core that finishes the build it was waiting for.
        if (!BlazorWebViewApp.WhenAppReady.IsCompleted) {
            var startedAt = CpuTimestamp.Now;
            BlazorWebViewApp.WhenAppReady.Wait();
            Log.LogWarning("Awaiting BlazorWebViewApp readiness blocked the UI thread for {Elapsed}",
                (CpuTimestamp.Now - startedAt).ToShortString());
        }

        var services = BlazorWebViewApp.Current.Services;
#if ANDROID
        var newMauiContext = new MauiContext(services, mauiContext.Context!);
#else
        var newMauiContext = new MauiContext(services);
#endif
        base.SetMauiContext(newMauiContext);
    }
}
