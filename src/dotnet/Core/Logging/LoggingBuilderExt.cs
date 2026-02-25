namespace ActualChat.Logging;

public static class LoggingBuilderExt
{
    public static ILoggingBuilder AddTailLogger(this ILoggingBuilder logging)
    {
        logging.Services.AddSingleton<ILoggerProvider>(c => new TailLoggerProvider(c))
            .AddSingleton<LogSinks>(_ => new LogSinks());
        return logging;
    }

    public static ILoggingBuilder AddSanitizingLoggerFactory(
        this ILoggingBuilder logging,
        Func<IServiceProvider, bool> mustSanitizePredicate)
    {
        logging.Services.AddSingleton<ILoggerFactory>(c => {
            var mustSanitize = mustSanitizePredicate.Invoke(c);
            var innerFactory = ActivatorUtilities.CreateInstance<LoggerFactory>(c);
            return new SanitizingLoggerFactory(innerFactory, mustSanitize);
        });
        return logging;
    }
}
