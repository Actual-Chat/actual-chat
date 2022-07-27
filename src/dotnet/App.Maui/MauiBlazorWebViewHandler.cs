using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler : BlazorWebViewHandler
{
    public string BaseUri
        => MauiContext!.Services.GetRequiredService<ClientAppSettings>().BaseUri.EnsureSuffix("/")
           ?? throw StandardError.Constraint<ClientAppSettings>("Invalid BaseUri.");
    public string SessionId
        => MauiContext!.Services.GetRequiredService<ClientAppSettings>().SessionId;

    public MauiBlazorWebViewHandler()
    {
        // Intentionally use parameterless constructor.
        // Constructor with parameters causes Exception on Android platform:
        // Microsoft.Maui.Platform.ToPlatformException
        // Message = Microsoft.Maui.Handlers.PageHandler found for ActualChat.App.Maui.MainPage is incompatible
    }
}
