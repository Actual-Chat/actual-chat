namespace ActualChat.UI.Blazor.Services;

public interface IBrowserInfoBackend
{
    void OnScreenSizeChanged(string screenSizeText, bool isHoverable, double windowHeight);
    void OnIsVisibleChanged(bool isVisible);
    void OnThemeChanged(ThemeInfo themeInfo);
    void OnThermalStateChanged(string state);
    void OnWebSplashRemoved();
    void OnWasmReady();

    // Nested types

    public sealed record InitResult(
        string ScreenSizeText,
        double WindowHeight,
        bool IsVisible,
        bool IsHoverable,
        ThemeInfo ThemeInfo,
        UILanguageInfo UILanguageInfo,
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
        bool CanVibrate,
        bool? IsWasmReady,
        string WindowId);

    public sealed record ThemeInfo(
        string? Theme,
        string DefaultTheme,
        string CurrentTheme,
        string Colors);

    public sealed record UILanguageInfo(
        string? Selected,
        string[] ClientLanguages);
}
