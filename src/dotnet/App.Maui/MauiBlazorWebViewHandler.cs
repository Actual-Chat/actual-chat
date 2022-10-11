using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler : BlazorWebViewHandler
{
    public ClientAppSettings AppSettings { get; private set; } = null!;
    public UrlMapper UrlMapper { get; private set; } = null!;

    public MauiBlazorWebViewHandler()
    {
        // Intentionally use parameterless constructor.
        // Constructor with parameters causes Exception on Android platform:
        // Microsoft.Maui.Platform.ToPlatformException
        // Message = Microsoft.Maui.Handlers.PageHandler found for ActualChat.App.Maui.MainPage is incompatible
    }

    public override void SetMauiContext(IMauiContext mauiContext)
    {
        base.SetMauiContext(mauiContext);
        AppSettings = mauiContext.Services.GetRequiredService<ClientAppSettings>();
        UrlMapper = mauiContext.Services.UrlMapper();
    }
}
