using ActualLab.Rpc;

namespace ActualChat;

public static class RpcHubExt
{
    public static Task WhenClientPeerConnected(this RpcHub rpcHub, CancellationToken cancellationToken = default)
    {
        var hostInfo = rpcHub.Services.HostInfo();
        if (!hostInfo.HostKind.IsApp())
            return Task.CompletedTask;

        var peer = rpcHub.GetClientPeer(RpcRef.Default);
        return peer.WhenConnected(cancellationToken);
    }
}
