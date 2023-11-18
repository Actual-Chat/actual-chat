namespace ActualChat.Security;

#pragma warning disable CA1721 // Session is confusing with GetSession

public sealed class DefaultSessionResolver(IServiceProvider services) : ISessionResolver
{
    private readonly Session _session = Session.Default;

    public IServiceProvider Services { get; } = services;
    public bool HasSession => true;
    public Session Session {
        get => _session;
        set => ArgumentOutOfRangeException.ThrowIfNotEqual(value, _session);
    }
    public Task<Session> SessionTask { get; } = Task.FromResult(Session.Default);

    public Task<Session> GetSession(CancellationToken cancellationToken = new CancellationToken())
        => SessionTask.WaitAsync(cancellationToken);
}
