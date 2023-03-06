namespace ActualChat.UI.Blazor.Services;

public interface IBrowserInfoBackend
{
    void OnScreenSizeChanged(string screenSizeText, bool isHoverable);

    void OnIsHiddenChanged(bool isHidden);

    // Nested types

    public sealed record InitResult(
        string ScreenSizeText,
        bool IsHidden,
        bool IsHoverable,
        double UtcOffset,
        bool IsMobile,
        bool IsAndroid,
        bool IsIos,
        bool IsChrome,
        bool IsTouchCapable,
        string WindowId);
}
