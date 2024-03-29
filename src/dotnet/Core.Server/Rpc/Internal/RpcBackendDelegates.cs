using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Rpc.Internal;

#pragma warning disable VSTHRD002, VSTHRD104

public sealed class RpcBackendDelegates(IServiceProvider services) : RpcServiceBase(services)
{
    private MeshNode MeshNode { get; } = services.MeshNode();
    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private BackendServiceDefs BackendServiceDefs { get; } = services.GetRequiredService<BackendServiceDefs>();
    private RpcMeshRefResolvers RpcMeshRefResolvers { get; } = services.GetRequiredService<RpcMeshRefResolvers>();

    public bool IsBackendService(Type serviceType)
        => BackendServiceDefs.Contains(serviceType)
            || typeof(IBackendService).IsAssignableFrom(serviceType)
            || serviceType.Name.EndsWith("Backend", StringComparison.Ordinal);

    public RpcPeer? GetPeer(RpcMethodDef methodDef, ArgumentList arguments)
    {
        var serviceDef = methodDef.Service;
        if (!serviceDef.IsBackend)
            throw StandardError.Internal("Only backend service methods can be called by servers.");

        var serverSideServiceDef = BackendServiceDefs[serviceDef.Type];
        var serviceMode = serverSideServiceDef.ServiceMode;
        if (serviceMode is not ServiceMode.Client and not ServiceMode.Hybrid)
            throw StandardError.Internal($"{serviceDef} must be a ServiceMode.Client or ServiceMode.RoutingServer mode service.");

        var meshRefResolver = RpcMeshRefResolvers[methodDef];
        var meshRef = meshRefResolver.Invoke(methodDef, arguments, serverSideServiceDef.ShardScheme);
        var peerRef = MeshWatcher.GetPeerRef(meshRef).Require(meshRef);
        if (serviceMode == ServiceMode.Hybrid) {
            // Such services expose a client which may route calls to the server on the same node,
            // so the code below speeds up such calls by returning null RpcPeer for them, which makes
            // ActualLab.Rpc infrastructure to call the server method directly for them instead.
            switch (peerRef) {
            case RpcBackendNodePeerRef nodePeerRef when nodePeerRef.NodeRef == MeshNode.Ref:
                return null; // It's this node, so we won't forward this call
            case RpcBackendShardPeerRef { Latest.WhenReady: { IsCompletedSuccessfully: true } nodePeerRefTask }:
                if (nodePeerRefTask.Result is { } shardNodePeerRef && shardNodePeerRef.NodeRef == MeshNode.Ref)
                    return null; // It's this node, so we won't forward this call
                break;
            }
        }
        return Hub.GetClientPeer(peerRef);
    }

#pragma warning disable CA1822 // Can be static
    public Task<RpcConnection> GetConnection(
        RpcServerPeer peer, Channel<RpcMessage> channel, ImmutableOptionSet options,
        CancellationToken cancellationToken)
#pragma warning restore CA1822
    {
        if (!options.TryGet<HttpContext>(out var httpContext))
            return RpcConnectionTask(channel, options);

        var session = httpContext.TryGetSessionFromHeader() ?? httpContext.TryGetSessionFromCookie();
        return session.IsValid()
            ? RpcBackendConnectionTask(channel, options, session)
            : RpcConnectionTask(channel, options);
    }

    private static Task<RpcConnection> RpcBackendConnectionTask(
        Channel<RpcMessage> channel, ImmutableOptionSet options, Session session)
        => Task.FromResult<RpcConnection>(new RpcBackendConnection(channel, options, session));

    private static Task<RpcConnection> RpcConnectionTask(
        Channel<RpcMessage> channel, ImmutableOptionSet options)
        => Task.FromResult(new RpcConnection(channel, options));
}
