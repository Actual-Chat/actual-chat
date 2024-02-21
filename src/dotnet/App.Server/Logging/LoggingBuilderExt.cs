using Serilog.Extensions.Logging;

namespace ActualChat.App.Server.Logging;

public static class LoggingBuilderExt
{
    public static ILoggingBuilder AddFilteringSerilog(
        this ILoggingBuilder builder,
        Serilog.ILogger? logger = null,
        bool dispose = false)
    {
        // NOTE(AY): It's almost the same code as in .AddSerilog, but with a single line commented out (see below)
        if (builder == null)
            throw new ArgumentNullException(nameof (builder));

        if (dispose)
            builder.Services.AddSingleton<ILoggerProvider, SerilogLoggerProvider>(_ => new SerilogLoggerProvider(logger, true));
        else
            builder.AddProvider(new SerilogLoggerProvider(logger));
        // builder.AddFilter<SerilogLoggerProvider>(null, LogLevel.Trace);
        return builder;
    }
}
