namespace ActualChat.App.Maui;

public class Bars
{
    public static readonly Bars Instance =
#if IOS
     new IosBars();
#elif ANDROID
    new AndroidBars();
#else
    new Bars();
#endif

    public virtual int GetStatusBarHeight()
        => 0;
}
