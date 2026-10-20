namespace ActualChat.UI.Blazor.App.Services.Gestures;

public sealed class PowerHushGesture
{
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(800);

    private long _screenOnAtMs = -1;

    public void OnScreenOn(long elapsedMilliseconds, bool isLocked)
        => Interlocked.Exchange(ref _screenOnAtMs, isLocked ? elapsedMilliseconds : -1);

    public void OnUnlocked()
        => Interlocked.Exchange(ref _screenOnAtMs, -1);

    public bool OnScreenOff(long elapsedMilliseconds)
    {
        var screenOnAtMs = Interlocked.Exchange(ref _screenOnAtMs, -1);
        var elapsed = elapsedMilliseconds - screenOnAtMs;
        return screenOnAtMs >= 0 && elapsed > 0 && elapsed < Window.TotalMilliseconds;
    }
}
