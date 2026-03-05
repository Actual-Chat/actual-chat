using ActualLab.Net;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.Services;

public sealed class AppRpcClientPeerReconnectDelayer(UIHub hub) : RpcClientPeerReconnectDelayer(hub.Services)
{
    private static readonly Moment InfMoment = Moment.Now + TimeSpan.FromDays(3650);

    public override RetryDelay GetDelay(
        RpcClientPeer peer, int tryIndex, Exception? lastError,
        CancellationToken cancellationToken = default)
        => hub.ConnectivityUI.IsOnline.Value
            ? base.GetDelay(peer, tryIndex, lastError, cancellationToken)
            : new RetryDelay(
                hub.ConnectivityUI.IsOnline.Computed.When(x => x, CancelDelaysToken).SuppressExceptions(),
                InfMoment);
}
