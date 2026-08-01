namespace ActualChat.UI.Blazor.Services;

public static class ExternalUrlOpenerExt
{
    public static Task OpenExternalUrl(this UIHub hub, string url)
        => hub.Services.GetService<IExternalUrlOpener>() is { } urlOpener
            ? urlOpener.Open(url)
            : hub.JS.OpenNewWindow(url).AsTask();
}
