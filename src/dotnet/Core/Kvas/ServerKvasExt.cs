namespace ActualChat.Kvas;

public static class ServerKvasExt
{
    public static Task Set(this IServerKvas serverKvas,
        Session session, string key, string? value,
        CancellationToken cancellationToken = default)
    {
        var cmd = new IServerKvas.SetCommand(session, key, value);
        return serverKvas.GetCommander().Call(cmd, true, cancellationToken);
    }

    public static Task SetMany(this IServerKvas serverKvas,
        Session session, (string Key, string? Value)[] items,
        CancellationToken cancellationToken = default)
    {
        var cmd = new IServerKvas.SetManyCommand(session, items);
        return serverKvas.GetCommander().Call(cmd, true, cancellationToken);
    }
}
