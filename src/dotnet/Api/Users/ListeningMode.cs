namespace ActualChat.Users;

public enum ListeningMode
{
    Default = 0,
    For5Minutes = 5,
    For15Minutes = 15,
    For1Hour = 60,
    Forever = 10_000,
}

public static class ListeningModeExt
{
    public static ListeningModeInfo GetInfo(this ListeningMode listeningMode)
        => ListeningModeInfo.Get(listeningMode) ?? ListeningModeInfo.Default;
}
