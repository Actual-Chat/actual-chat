using Stl.Internal;
using Stl.Rpc;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class TrueSessionResolver : ISessionResolver
{
    private readonly object _lock = new();
    private volatile TaskCompletionSource<Session> _sessionSource = TaskCompletionSourceExt.New<Session>();
    private volatile Session? _session;

    public IServiceProvider Services { get; }
    public bool HasSession => _session != null;
    public Task<Session> SessionTask => _sessionSource.Task;
    public Session Session {
        get => _session ?? throw Errors.NotInitialized(nameof(Session));
        set {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            lock (_lock) {
                if (_session == value)
                    return;
                if (_session != null)
                    throw Errors.AlreadyInitialized(nameof(Session));

                _session = value;
                _sessionSource.TrySetResult(value);
            }
            Services.RpcHub().GetClientPeer(RpcPeerRef.Default).Disconnect();
        }
    }

    public TrueSessionResolver(IServiceProvider services)
        => Services = services;

    public Task<Session> GetSession(CancellationToken cancellationToken = default)
        => SessionTask.WaitAsync(cancellationToken);

    public void Replace(Session value)
    {
        lock (_lock) {
            if (_session == value)
                return;

            _session = value;
            _sessionSource = TaskCompletionSourceExt.New<Session>().WithResult(value);
        }
        Services.RpcHub().GetClientPeer(RpcPeerRef.Default).Disconnect();
    }
}
