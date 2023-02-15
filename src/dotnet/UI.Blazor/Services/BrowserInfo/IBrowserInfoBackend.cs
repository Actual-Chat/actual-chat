namespace ActualChat.UI.Blazor.Services;

public interface IBrowserInfoBackend
{
    void OnScreenSizeChanged(string screenSizeText, bool isHoverable);

    // Nested types

    public sealed record InitResult(
        string ScreenSizeText,
        bool IsHoverable,
        double UtcOffset,
        bool IsMobile,
        bool IsAndroid,
        bool IsIos,
        bool IsChrome,
        bool IsTouchCapable,
        string WindowId);
}
