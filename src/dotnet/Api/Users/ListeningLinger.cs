namespace ActualChat.Users;

/// <summary>
/// How long listening keeps running after a chat's conversation goes quiet.
/// </summary>
public enum ListeningLinger
{
    None = 0,
    For10Seconds = 10,
    For30Seconds = 30,
    For1Minute = 60,
}

public static class ListeningLingerExt
{
    public static TimeSpan ToTimeSpan(this ListeningLinger value)
        => TimeSpan.FromSeconds((int)value);
}
