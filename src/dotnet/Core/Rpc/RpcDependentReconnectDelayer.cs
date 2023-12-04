using Stl.Net;
using Stl.Rpc;
using Stl.Rpc.Infrastructure;

namespace ActualChat.Rpc;

public sealed class RpcDependentReconnectDelayer : RpcServiceBase, IRetryDelayer
{
    private RpcClientPeerReconnectDelayer ClientPeerReconnectDelayer { get; }

    public RpcPeerRef PeerRef { get; init; } = RpcPeerRef.Default;
    public IMomentClock Clock => ClientPeerReconnectDelayer.Clock;
    public CancellationToken CancelDelaysToken => ClientPeerReconnectDelayer.CancelDelaysToken;

    public RpcDependentReconnectDelayer(IServiceProvider services)
        : base(services)
        => ClientPeerReconnectDelayer = Hub.InternalServices.ClientPeerReconnectDelayer;

    public Task WhenDisconnected(CancellationToken cancellationToken)
    {
        var peer = Hub.GetClientPeer(PeerRef);
        var connectionState = peer.ConnectionState;
        return connectionState.Value.IsConnected()
            ? connectionState.When(x => !x.IsConnected(), cancellationToken)
            : Task.CompletedTask;
    }

    public Task WhenConnected(CancellationToken cancellationToken)
    {
        var peer = Hub.GetClientPeer(PeerRef);
        var connectionState = peer.ConnectionState;
        return connectionState.Value.IsConnected() ? Task.CompletedTask : WhenConnectedImpl();

        async Task WhenConnectedImpl()
        {
            while (true) {
                try {
                    await connectionState.When(x => x.IsConnected(), cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                }
#pragma warning disable RCS1075 // Avoid empty catch clause that catches System.Exception.
                catch (Exception) {
                    // Intended
                }
#pragma warning restore RCS1075

                // We want to avoid high CPU consumption on possible rapid iterations here
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                peer = Hub.GetClientPeer(PeerRef);
                connectionState = peer.ConnectionState;
            }
        }
    }

    public void CancelDelays()
        => ClientPeerReconnectDelayer.CancelDelays();

    public RetryDelay GetDelay(int tryIndex, CancellationToken cancellationToken = default)
    {
        var whenConnected = WhenConnected(cancellationToken);
        return whenConnected.IsCompletedSuccessfully
            ? RetryDelay.None
            : new RetryDelay(whenConnected, Clock.Now + TimeSpan.FromHours(1)); // EndsAt should be just big enough here
    }
}
