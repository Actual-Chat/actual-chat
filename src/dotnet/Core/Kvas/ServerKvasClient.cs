namespace ActualChat.Kvas;

public class ServerKvasClient : IKvas
{
    public IServerKvas ServerKvas { get; }
    public Session Session { get; }

    public ServerKvasClient(IServerKvas serverKvas, Session session)
    {
        ServerKvas = serverKvas;
        Session = session;
    }

    public async ValueTask<string?> Get(string key, CancellationToken cancellationToken = default)
    {
        var result = await ServerKvas.Get(Session, key, cancellationToken).ConfigureAwait(false);
        return result.IsSome(out var value) ? value : null;
    }

    public Task Set(string key, string? value, CancellationToken cancellationToken = default)
        => ServerKvas.Set(Session, key, value, cancellationToken);

    public Task SetMany((string Key, string? Value)[] items, CancellationToken cancellationToken = default)
        => ServerKvas.SetMany(Session, items, cancellationToken);
}
