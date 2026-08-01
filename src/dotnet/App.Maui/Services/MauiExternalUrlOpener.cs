using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public sealed class MauiExternalUrlOpener : IExternalUrlOpener
{
    public Task Open(string url)
        => Browser.Default.OpenAsync(url, BrowserLaunchMode.External);
}
