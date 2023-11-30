namespace ActualChat.UI.Blazor.Services;

public enum ActivityState
{
    Foreground = 0,
    BackgroundIdle,
    BackgroundActive,
}

public static class ActivityStateExt
{
    public static bool IsForeground(this ActivityState state)
        => state == ActivityState.Foreground;

    public static bool IsBackground(this ActivityState state)
        => state != ActivityState.Foreground;
}
