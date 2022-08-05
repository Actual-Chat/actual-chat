namespace ActualChat.Kvas;

public class ServerKvasClient : IKvas
{
    public IServerKvas Upstream { get; }
    public Session Session { get; }

    public ServerKvasClient(IServerKvas upstream, Session session)
    {
        Upstream = upstream;
        Session = session;
    }

    public ValueTask<string?> Get(string key, CancellationToken cancellationToken = default)
        => Upstream.Get(Session, key, cancellationToken).ToValueTask();

    public Task Set(string key, string? value, CancellationToken cancellationToken = default)
        => Upstream.Set(Session, key, value, cancellationToken);

    public Task SetMany((string Key, string? Value)[] items, CancellationToken cancellationToken = default)
        => Upstream.SetMany(Session, items, cancellationToken);
}
