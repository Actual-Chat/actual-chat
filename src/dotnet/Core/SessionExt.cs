namespace ActualChat;

public static class SessionExt
{
    public static Session? NewValidOrNull(string? sessionId)
    {
        if (sessionId.IsNullOrEmpty())
            return null;

        try {
            return new Session(sessionId).NullIfInvalid();
        }
        catch {
            return null;
        }
    }

    public static Session? NullIfInvalid(this Session? session)
        => session.IsValid() ? session : null;

    public static string GetPrefix(this Session session)
        => session.Id.Length >= CoreConstants.Session.IdPrefixLength
            ? session.Id[..CoreConstants.Session.IdPrefixLength]
            : session.Id;

    public static bool IsApiKey(this Session session)
        => session.Id.StartsWith(CoreConstants.Session.ApiKeyPrefix, StringComparison.Ordinal);
}
