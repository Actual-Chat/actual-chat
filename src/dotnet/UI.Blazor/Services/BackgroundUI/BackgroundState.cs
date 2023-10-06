namespace ActualChat.UI.Blazor.Services;

public enum BackgroundState
{
    Foreground = 0b00,
    BackgroundActive = 0b10,
    BackgroundIdle = 0b11,
}

public static class BackgroundStateExt
{
    public static bool IsActive(this BackgroundState state)
        => ((int)state & 0b01) == 0;

    public static bool IsBackground(this BackgroundState state)
        => ((int)state & 0b10) != 0;
}
