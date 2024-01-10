using ActualChat.UI.Blazor.App.Pages.Test;

namespace ActualChat.App.Maui;

public class AndroidWebViewCrasher: IWebViewCrasher
{
    public void Crash()
    {
        if (MauiWebView.Current is { } mauiWebView && mauiWebView.AndroidWebView.IfNotNull() is { } androidWebView)
            androidWebView.LoadUrl("chrome://crash");
    }
}
