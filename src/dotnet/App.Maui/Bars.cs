namespace ActualChat.App.Maui;

public class Bars
{
#if IOS
    public static readonly IosBars Instance = new IosBars();
#elif ANDROID
    public static readonly AndroidBars Instance = new AndroidBars();
#else
    public static readonly Bars Instance = new Bars();
#endif

    public virtual int GetStatusBarHeight()
        => 0;
}
