namespace ActualChat.UI.Blazor.Services;

public interface IBrowserInfoBackend
{
    void OnScreenSizeChanged(string screenSizeText, bool isHoverable);
    void OnIsVisibleChanged(bool isVisible);
    void OnThemeChanged(ThemeInfo themeInfo);
    void OnThermalStateChanged(string state);
    void OnWebSplashRemoved();
    void OnWasmReady();

    // Nested types

    public sealed record InitResult(
        string ScreenSizeText,
        bool IsVisible,
        bool IsHoverable,
        ThemeInfo ThemeInfo,
        string DefaultTheme,
        double UtcOffset,
        string TimeZone,
        bool IsMobile,
        bool IsAndroid,
        bool IsIos,
        bool IsMacOS,
        bool IsChromium,
        bool IsEdge,
        bool IsWebKit,
        bool IsTouchCapable,
        bool? IsWasmReady,
        string WindowId);

    public sealed record ThemeInfo(
        string? Theme,
        string DefaultTheme,
        string CurrentTheme,
        string Colors);
}
