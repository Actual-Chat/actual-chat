using Stl.Net;
using Stl.Rpc;

namespace ActualChat.Rpc;

public class RpcClientPeerDependentReconnectDelayer : IRetryDelayer
{
    private readonly RpcClientPeerReconnectDelayer _clientPeerReconnectDelayer;
    private readonly RpcClientPeer _clientPeer;

    public IMomentClock Clock => _clientPeerReconnectDelayer.Clock;
    public CancellationToken CancelDelaysToken => _clientPeerReconnectDelayer.CancelDelaysToken;

    public RpcClientPeerDependentReconnectDelayer(IServiceProvider services)
    {
        var hub = services.RpcHub();
        _clientPeerReconnectDelayer = hub.InternalServices.ClientPeerReconnectDelayer;
        _clientPeer = (RpcClientPeer)hub.GetPeer(RpcPeerRef.Default);
    }

    public void CancelDelays()
        => _clientPeerReconnectDelayer.CancelDelays();

    public RetryDelay GetDelay(int tryIndex, CancellationToken cancellationToken = default)
    {
        var connectionState = _clientPeer.ConnectionState;
        return connectionState.Value.IsConnected()
            ? RetryDelay.None
            : new RetryDelay(DelayImpl(), Clock.Now + _clientPeerReconnectDelayer.Delays.Max);

        async Task DelayImpl()
        {
            var cancelDelaysToken = CancelDelaysToken;
            var cts = cancellationToken.LinkWith(cancelDelaysToken);
            try {
                await connectionState.When(x => x.IsConnected(), cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancelDelaysToken.IsCancellationRequested) {
                // Intended: the delay is cancelled
            }
            finally {
                cts.Dispose();
            }
        }
    }
}
