namespace ActualChat.App.Maui;

public static class AndroidConstants
{
#if DEBUG
    public const string LogTag = "dev.actual.chat";
#else
    public const string LogTag = "actual.chat";
#endif
}
