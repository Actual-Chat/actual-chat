namespace ActualChat.UI.Blazor.Services;

public interface IBrowserInfoBackend
{
    void OnScreenSizeChanged(string screenSizeText, bool isHoverable);
    void OnIsVisibleChanged(bool isVisible);
    void OnDefaultThemeChanged(string defaultTheme);

    // Nested types

    public sealed record InitResult(
        string ScreenSizeText,
        bool IsVisible,
        bool IsHoverable,
        string? Theme,
        string DefaultTheme,
        double UtcOffset,
        bool IsMobile,
        bool IsAndroid,
        bool IsIos,
        bool IsChromium,
        bool IsEdge,
        bool IsWebKit,
        bool IsTouchCapable,
        string WindowId);
}
