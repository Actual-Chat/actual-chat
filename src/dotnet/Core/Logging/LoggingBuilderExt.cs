using Microsoft.Extensions.Options;

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
        IEnumerable<ILoggerProvider>? providers = null;
        IOptionsMonitor<LoggerFilterOptions>? filterOption = null;
        IOptions<LoggerFactoryOptions>? options = null;
        logging.Services.AddSingleton<ILoggerFactory>(c => {
            var mustSanitize = mustSanitizePredicate.Invoke(c);
            providers ??= c.GetServices<ILoggerProvider>();
            filterOption ??= c.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>();
            options ??= c.GetService<IOptions<LoggerFactoryOptions>>();
            var scopeProvider = c.GetService<IExternalScopeProvider>();
            var innerFactory = new LoggerFactory(providers, filterOption, options, scopeProvider);
            return mustSanitize
                ? new SanitizingLoggerFactory(innerFactory)
                : innerFactory;
        });
        return logging;
    }
}
