using ActualChat.Hosting;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat;

public static class RpcHubExt
{
    public static Task WhenClientPeerConnected(this RpcHub rpcHub, CancellationToken cancellationToken = default)
    {
        var hostInfo = rpcHub.Services.HostInfo();
        if (!hostInfo.AppKind.IsClient())
            return Task.CompletedTask;

        var peer = rpcHub.GetClientPeer(RpcPeerRef.Default);
        return peer.ConnectionState.WhenConnected(cancellationToken);
    }
}
