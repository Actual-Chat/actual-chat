namespace ActualChat.Logging;

public static class LegacyApiUsageLog
{
    public static void Write(
        ILogger log,
        string entryPoint,
        Session session,
        string? clientInfo,
        string? details = null)
    {
        var clientVersion = GetClientVersion(clientInfo);
        log.LogWarning(
            "Legacy API {EntryPoint} called by session {SessionId}; client version: {ClientVersion}; details: {Details}",
            entryPoint,
            session.Id,
            clientVersion,
            details ?? "");
    }

    private static string GetClientVersion(string? clientInfo)
    {
        if (clientInfo.IsNullOrWhiteSpace())
            return "unknown";

        var separatorIndex = clientInfo.IndexOf(' ');
        return separatorIndex < 0 ? clientInfo : clientInfo[..separatorIndex];
    }
}
