using ActualChat.Mesh;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;

namespace ActualChat.Rpc;

public class RpcBackendWebSocketClient(RpcWebSocketClient.Options settings, IServiceProvider services)
    : RpcWebSocketClient(settings, services)
{
    // private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();

    public override async Task<RpcConnection> CreateConnection(RpcClientPeer peer, CancellationToken cancellationToken)
    {
        switch (peer.Ref) {
        case RpcBackendNodePeerRef nodePeerRef: {
            if (nodePeerRef.StopToken.IsCancellationRequested)
                throw MeshNodeIsGoneError();

            var meshNode = await nodePeerRef.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (meshNode == null)
                throw MeshNodeIsGoneError();

            return await CreateConnection(peer, meshNode, cancellationToken).ConfigureAwait(false);
        }
        case RpcBackendShardPeerRef shardPeerRef: {
            retry:
            var shardNodePeerRef = await shardPeerRef.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (shardNodePeerRef == null)
                throw InvalidShardRefError();
            if (shardNodePeerRef.StopToken.IsCancellationRequested) {
                shardPeerRef = shardPeerRef.Latest;
                goto retry;
            }

            var meshNode = await shardNodePeerRef.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (meshNode == null) {
                shardPeerRef = shardPeerRef.Latest;
                goto retry;
            }

            return await CreateConnection(peer, meshNode, cancellationToken).ConfigureAwait(false);
        }
        default:
            throw InvalidPeerRefTypeError();
        }
    }

    // Private methods

    private Task<RpcConnection> CreateConnection(RpcClientPeer peer, MeshNode meshNode, CancellationToken cancellationToken)
    {
        var sb = StringBuilderExt.Acquire();
        sb.Append("ws://");
        sb.Append(meshNode.Endpoint);
        sb.Append(Settings.BackendRequestPath);
        sb.Append('?');
        sb.Append(Settings.ClientIdParameterName);
        sb.Append('=');
        sb.Append(ClientId.UrlEncode());
        var uri = sb.ToStringAndRelease().ToUri();
        return CreateConnection(peer, uri, cancellationToken);
    }

    private static Exception InvalidPeerRefTypeError()
        => new ConnectionUnrecoverableException("Invalid RpcPeerRef type.");

    private static Exception InvalidShardRefError()
        => new ConnectionUnrecoverableException("ShardRef is invalid or current process is shutting down.");

    private static Exception MeshNodeIsGoneError()
        => new ConnectionUnrecoverableException("Required MeshNode is gone.");
}
