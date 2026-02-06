namespace ActualChat.Users;

/// <summary>
/// Specifies the duration for listening to a chat.
/// </summary>
public enum ListeningMode
{
    Default = 0,
    For5Minutes = 5,
    For15Minutes = 15,
    For1Hour = 60,
    Forever = 10_000,
}

/// <summary>
/// Extension methods for <see cref="ListeningMode"/>.
/// </summary>
public static class ListeningModeExt
{
    public static ListeningModeInfo GetInfo(this ListeningMode listeningMode)
        => ListeningModeInfo.Get(listeningMode) ?? ListeningModeInfo.Default;
}
