namespace ActualChat.Kvas;

public class KvasClient : IKvas
{
    public IServerKvas ServerKvas { get; }
    public Session Session { get; }

    public KvasClient(IServerKvas serverKvas, Session session)
    {
        ServerKvas = serverKvas;
        Session = session;
    }

    public ValueTask<string?> Get(string key, CancellationToken cancellationToken = default)
        => ServerKvas.Get(Session, key, cancellationToken).ToValueTask();

    public Task Set(string key, string? value, CancellationToken cancellationToken = default)
        => ServerKvas.Set(Session, key, value, cancellationToken);

    public Task SetMany((string Key, string? Value)[] items, CancellationToken cancellationToken = default)
        => ServerKvas.SetMany(Session, items, cancellationToken);
}
