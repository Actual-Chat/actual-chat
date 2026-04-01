using Microsoft.Extensions.Hosting;

namespace ActualChat.Logging;

public static class LoggerExt
{
    public static ILogger? UnlessStopping(this ILogger? logger, IHostApplicationLifetime? hostLifetime)
        => logger is not null && (hostLifetime is null || !hostLifetime.ApplicationStopping.IsCancellationRequested)
            ? logger
            : null;
}
