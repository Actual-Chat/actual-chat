namespace ActualChat.UI.Blazor.Services;

public enum BackgroundState
{
    Foreground = 0,
    BackgroundIdle,
    BackgroundActive,
}

public static class BackgroundStateExt
{
    public static bool IsForeground(this BackgroundState state)
        => state == BackgroundState.Foreground;

    public static bool IsBackground(this BackgroundState state)
        => state != BackgroundState.Foreground;
}
