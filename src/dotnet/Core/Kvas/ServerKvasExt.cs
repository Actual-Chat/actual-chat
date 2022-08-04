namespace ActualChat.Kvas;

public static class ServerKvasExt
{
    public static IKvas WithSession(this IServerKvas serverKvas, Session session)
        => new ServerKvasWrapper(serverKvas, session);
}
