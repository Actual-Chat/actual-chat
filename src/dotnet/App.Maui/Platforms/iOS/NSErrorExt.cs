using Foundation;

namespace ActualChat.App.Maui;

public static class NSErrorExt
{
    public static void Assert(this NSError? err)
    {
        if (err != null)
            throw new NSErrorException(err);
    }

    public static void Log(this NSError? err, string message, ILogger? logger = null, LogLevel level = LogLevel.Error)
    {
        if (err is null)
            return;

        logger ??= DefaultLog;
        logger.Log(level, "{Message}: {NSError}", message, err);
    }
}
