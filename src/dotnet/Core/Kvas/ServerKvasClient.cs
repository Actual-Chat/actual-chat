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

    public ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
        => ServerKvas.Get(Session, key, cancellationToken).ToValueTask();

    public Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
        => ServerKvas.Set(Session, key, value, cancellationToken);

    public Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
        => ServerKvas.SetMany(Session, items, cancellationToken);
}
