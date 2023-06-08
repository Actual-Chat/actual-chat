using Sentry;

namespace ActualChat.UI.Blazor;

public static class SentryExt
{
    private const string UIDsn = "https://7bcdf3ac9a774dfab54df0e0a9865a20@o4504632882233344.ingest.sentry.io/4504639283789824";

    public static void ConfigureForApp(this SentryOptions options)
    {
        options.Dsn = UIDsn;
        options.AddExceptionFilterForType<OperationCanceledException>();
        options.Debug = false;
        options.DiagnosticLevel = SentryLevel.Error;
    }
}
