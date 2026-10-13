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
        var services = BlazorWebViewApp.WaitForServices(Log);
#if ANDROID
        var newMauiContext = new MauiContext(services, mauiContext.Context!);
#else
        var newMauiContext = new MauiContext(services);
#endif
        base.SetMauiContext(newMauiContext);
    }
}
