namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Opens a URL outside the app. Overridden where <c>window.open</c> doesn't work -
/// in MAUI it routes to OnCreateWindow, which throws on a programmatic open.
/// </summary>
public class ExternalUrlOpener(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    public virtual Task Open(string url)
        => JS.OpenNewWindow(url).AsTask();
}
