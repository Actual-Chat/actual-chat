namespace ActualChat.Kvas;

public static class ServerKvasExt
{
    public static IKvas<User> GetClient(this IServerKvas serverKvas, Session session)
        => new ServerKvasClient(serverKvas, session);

    public static Task Set(this IServerKvas serverKvas,
        Session session, string key, byte[]? value,
        CancellationToken cancellationToken = default)
    {
        var command = new ServerKvas_Set(session, key, value);
        return serverKvas.GetCommander().Call(command, true, cancellationToken);
    }

    public static Task SetMany(this IServerKvas serverKvas,
        Session session, (string Key, byte[]? Value)[] items,
        CancellationToken cancellationToken = default)
    {
        var command = new ServerKvas_SetMany(session, items);
        return serverKvas.GetCommander().Call(command, true, cancellationToken);
    }
}
