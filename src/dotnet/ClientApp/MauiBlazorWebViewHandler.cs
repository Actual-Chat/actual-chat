using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.ClientApp;

public partial class MauiBlazorWebViewHandler : BlazorWebViewHandler
{
    protected readonly ClientAppSettings _settings;
    public MauiBlazorWebViewHandler(ClientAppSettings settings, PropertyMapper? mapper = null) : base(mapper)
    {
        _settings = settings;
    }
}
