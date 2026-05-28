namespace ActualChat.App.Maui;

/// <summary>
/// Provides platform-specific status bar height information for mobile devices.
/// </summary>
public class Bars
{
    public static readonly Bars Instance =
#if IOS
     new AppleBars();
#elif ANDROID
    new AndroidBars();
#else
    new Bars();
#endif

    public virtual int GetStatusBarHeight()
        => 0;
}
