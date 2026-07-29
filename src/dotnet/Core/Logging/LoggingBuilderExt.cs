namespace ActualChat.Logging;

public static class LoggingBuilderExt
{
    public static ILoggingBuilder AddTailLogger(this ILoggingBuilder logging)
    {
        var services = logging.Services;
        services.AddSingleton<ILoggerProvider>(c => new TailLoggerProvider(c));
        services.AddSingleton<TailLoggerSinkSet>(_ => new TailLoggerSinkSet());
        return logging;
    }
}
