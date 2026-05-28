namespace ActualChat.Maui;

public static class LoggingBuilderExt
{
    public static ILoggingBuilder AddAppleUnifiedLog(this ILoggingBuilder builder)
        => builder.AddProvider(new OSLogLoggerProvider());
}
