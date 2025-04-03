namespace ActualChat.Logging;

public static class TailLoggerServiceCollectionExt
{
    public static ILoggingBuilder AddTailLogger(this ILoggingBuilder loggingBuilder)
    {
        loggingBuilder.Services.AddSingleton<ILoggerProvider>(c => new TailLoggerProvider(c)).AddSingleton<LogSinks>();
        return loggingBuilder;
    }
}

