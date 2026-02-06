namespace ActualChat.UI.Blazor.Services;

public enum AppActivityState
{
    Foreground = 0,
    BackgroundIdle,
    BackgroundActive,
}

public static class AppActivityStateExt
{
    public static bool IsForeground(this AppActivityState state)
        => state == AppActivityState.Foreground;

    public static bool IsBackground(this AppActivityState state)
        => state != AppActivityState.Foreground;
}
