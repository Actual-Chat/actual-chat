using ActualLab.Net;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.Services;

public sealed class AppRpcClientPeerReconnectDelayer(IServiceProvider services)
    : RpcClientPeerReconnectDelayer(services)
{
    private static readonly Moment InfMoment = Moment.Now + TimeSpan.FromDays(3650);

    private volatile Func<bool> _isOnlineResolver = () => true;

    public Func<bool> IsOnlineResolver {
        get => _isOnlineResolver;
        set {
            Interlocked.Exchange(ref _isOnlineResolver, value);
            CancelDelays();
        }
    }

    public override RetryDelay GetDelay(
        RpcClientPeer peer, int tryIndex, Exception? lastError,
        CancellationToken cancellationToken = default)
        => IsOnlineResolver.Invoke()
            ? base.GetDelay(peer, tryIndex, lastError, cancellationToken)
            : new RetryDelay(
                TaskExt.NeverEnding(CancelDelaysToken).SuppressExceptions(),
                InfMoment);
}
