namespace ActualChat.Security;

public sealed class DefaultSessionResolver : ISessionResolver
{
    private readonly Session _session = Session.Default;

    public IServiceProvider Services { get; }
    public bool HasSession => true;
    public Session Session {
        get => _session;
        set {
            if (value != _session)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
    public Task<Session> SessionTask { get; } = Task.FromResult(Session.Default);

    public DefaultSessionResolver(IServiceProvider services)
        => Services = services;

    public Task<Session> GetSession(CancellationToken cancellationToken = new CancellationToken())
        => SessionTask.WaitAsync(cancellationToken);
}
