using Microsoft.Extensions.Hosting;

namespace ActualChat.Logging;

public static class LoggerExt
{
    public static bool MustLogLegacyApiUsage { get; set; } = false;

    public static ILogger? UnlessStopping(this ILogger? logger, IHostApplicationLifetime? hostLifetime)
        => logger is not null && (hostLifetime is null || !hostLifetime.ApplicationStopping.IsCancellationRequested)
            ? logger
            : null;

    public static void LogLegacyApiUsage(
        this ILogger log,
        string entryPoint,
        Session session,
        string? clientInfo,
        string? details = null)
    {
        if (!MustLogLegacyApiUsage)
            return;

        log.LogWarning(
            "Legacy API {EntryPoint} called by session {SessionId}; client version: {ClientVersion}; details: {Details}",
            entryPoint,
            session.Id,
            GetClientVersion(clientInfo),
            details ?? "");
    }

    // Private methods

    private static string GetClientVersion(string? clientInfo)
    {
        if (clientInfo.IsNullOrWhiteSpace())
            return "unknown";

        var separatorIndex = clientInfo.IndexOf(' ');
        return separatorIndex < 0 ? clientInfo : clientInfo[..separatorIndex];
    }
}
