namespace ActualChat.Users;

/// <summary>
/// How long listening continues after voice activity in a chat ends.
/// </summary>
public enum ContinuedListening
{
    None = 0,
    For10Seconds = 10,
    For30Seconds = 30,
    For1Minute = 60,
}

public static class ContinuedListeningExt
{
    public static TimeSpan ToTimeSpan(this ContinuedListening value)
        => TimeSpan.FromSeconds((int)value);
}
