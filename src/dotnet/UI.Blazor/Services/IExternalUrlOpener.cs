namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Opens a URL outside the app. Implemented only where window.open doesn't work -
/// in MAUI it routes to OnCreateWindow, which throws on a programmatic open.
/// </summary>
public interface IExternalUrlOpener
{
    Task Open(string url);
}
