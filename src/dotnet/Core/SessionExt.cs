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
}
