namespace ActualChat.UI.Blazor.Services;

public interface IBrowserInfoBackend
{
    void OnScreenSizeChanged(string screenSizeText);
    void OnRedirect(string url);

    // Nested types

    public sealed record InitResult(
        string ScreenSizeText,
        double UtcOffset,
        bool IsMobile,
        bool IsAndroid,
        bool IsIos,
        bool IsChrome,
        bool IsTouchCapable,
        string WindowId);
}
