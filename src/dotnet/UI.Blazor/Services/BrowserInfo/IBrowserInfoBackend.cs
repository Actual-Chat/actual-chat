namespace ActualChat.UI.Blazor.Services;

public interface IBrowserInfoBackend
{
    void OnScreenSizeChanged(string screenSizeText);

    // Nested types

    public sealed record InitResult(
        string ScreenSizeText,
        double UtcOffset,
        bool IsTouchCapable,
        string WindowId);
}
