namespace ActualChat.App.Maui;

// The content cache's native adapters aren't DI-constructed, so they share this logger
internal static class ContentCacheLog
{
    private static bool DebugMode => CoreConstants.DebugMode.ContentCache;

    public static ILogger Log => field ??= StaticLog.For(typeof(ContentCacheLog));
    public static ILogger? DebugLog => DebugMode ? Log : null;
}
