namespace ActualChat.Kvas;

public static class IServerKvasExt
{
    public static IKvas GetClient(this IServerKvas serverKvas, Session session)
        => new ServerKvasClient(serverKvas, session);

}
