namespace ActualChat.UI.Blazor.Services;

public interface IBrowserInfoBackend
{
    void OnScreenSizeChanged(string screenSizeText, bool isHoverable);
    void OnIsVisibleChanged(bool isVisible);
    void OnThemeChanged(ThemeInfo themeInfo);
    void OnWebSplashRemoved();

    // Nested types

    public sealed record InitResult(
        string ScreenSizeText,
        bool IsVisible,
        bool IsHoverable,
        ThemeInfo ThemeInfo,
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

    public sealed record ThemeInfo(
        string? Theme,
        string DefaultTheme,
        string CurrentTheme,
        string Colors);
}
