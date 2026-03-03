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

    public static ILoggingBuilder AddSanitizingLoggerFactory(
        this ILoggingBuilder logging,
        Func<IServiceProvider, bool> mustSanitizePredicate)
    {
        logging.Services.AddSingleton<ILoggerFactory>(c => {
            var mustSanitize = mustSanitizePredicate.Invoke(c);
            var innerFactory = ActivatorUtilities.CreateInstance<LoggerFactory>(c);
            return mustSanitize
                ? new SanitizingLoggerFactory(innerFactory)
                : innerFactory;
        });
        return logging;
    }
}
