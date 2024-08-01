using ActualChat.Mesh;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;

namespace ActualChat.Rpc.Internal;

public class RpcBackendWebSocketClient(RpcWebSocketClient.Options settings, IServiceProvider services)
    : RpcWebSocketClient(settings, services)
{
    // private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();

    public override async Task<RpcConnection> ConnectRemote(
        RpcClientPeer peer, Uri? uri, CancellationToken cancellationToken)
    {
        switch (peer.Ref) {
        case RpcBackendNodePeerRef nodePeerRef: {
            if (nodePeerRef.StopToken.IsCancellationRequested)
                throw MeshNodeIsGoneError();

            var meshNode = await nodePeerRef.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (meshNode == null)
                throw MeshNodeIsGoneError();

            return await ConnectRemote(peer, meshNode, cancellationToken).ConfigureAwait(false);
        }
        case RpcBackendShardPeerRef shardPeerRef: {
            retry:
            shardPeerRef = shardPeerRef.Latest;
            var shardNodePeerRef = await shardPeerRef.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (shardNodePeerRef == null)
                throw InvalidShardRefError();

            if (shardNodePeerRef.StopToken.IsCancellationRequested)
                await shardPeerRef.WhenObsolete.ConfigureAwait(false);
            if (shardPeerRef.WhenObsolete.IsCompleted)
                goto retry;

            var meshNode = await shardNodePeerRef.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (meshNode == null)
                goto retry;

            return await ConnectRemote(peer, meshNode, cancellationToken).ConfigureAwait(false);
        }
        default:
            throw InvalidPeerRefTypeError();
        }
    }

    // Private methods

    private Task<RpcConnection> ConnectRemote(RpcClientPeer peer, MeshNode meshNode, CancellationToken cancellationToken)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append("ws://");
        sb.Append(meshNode.Endpoint);
        sb.Append(Settings.BackendRequestPath);
        sb.Append('?');
        sb.Append(Settings.ClientIdParameterName);
        sb.Append('=');
        sb.Append(peer.ClientId); // Always Url-encoded
        var uri = sb.ToStringAndRelease().ToUri();
        return base.ConnectRemote(peer, uri, cancellationToken);
    }

    private static Exception InvalidPeerRefTypeError()
        => new RpcReconnectFailedException("Invalid RpcPeerRef type.");

    private static Exception InvalidShardRefError()
        => new RpcReconnectFailedException("ShardRef is invalid or current process is shutting down.");

    private static Exception MeshNodeIsGoneError()
        => new RpcReconnectFailedException("Required MeshNode is gone.");
}
