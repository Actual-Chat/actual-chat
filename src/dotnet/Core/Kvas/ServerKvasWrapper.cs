namespace ActualChat.Kvas;

public class ServerKvasWrapper : IKvas
{
    public IServerKvas Upstream { get; }
    public Session Session { get; }

    public ServerKvasWrapper(IServerKvas upstream, Session session)
    {
        Upstream = upstream;
        Session = session;
    }

    public ValueTask<string?> Get(string key, CancellationToken cancellationToken = default)
        => Upstream.Get(Session, key, cancellationToken).ToValueTask();

    public Task Set(string key, string? value, CancellationToken cancellationToken = default)
        => Upstream.Set(new IServerKvas.SetCommand(Session, key, value), cancellationToken);

    public Task SetMany((string Key, string? Value)[] items, CancellationToken cancellationToken = default)
        => Upstream.SetMany(new IServerKvas.SetManyCommand(Session, items), cancellationToken);
}
