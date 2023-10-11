namespace ActualChat.Kvas;

public class ServerKvasClient(IServerKvas serverKvas, Session session) : IKvas
{
    public IServerKvas ServerKvas { get; } = serverKvas;
    public Session Session { get; } = session;

    public ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
        => ServerKvas.Get(Session, key, cancellationToken).ToValueTask();

    public Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
        => ServerKvas.Set(Session, key, value, cancellationToken);

    public Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
        => ServerKvas.SetMany(Session, items, cancellationToken);
}
