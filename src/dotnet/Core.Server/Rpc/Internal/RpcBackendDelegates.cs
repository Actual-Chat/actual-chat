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
    private volatile TaskCompletionSource? _whenRouting = new();

    private MeshNode MeshNode { get; } = services.MeshNode();
    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private BackendServiceDefs BackendServiceDefs { get; } = services.GetRequiredService<BackendServiceDefs>();
    private RpcMeshRefResolvers RpcMeshRefResolvers { get; } = services.GetRequiredService<RpcMeshRefResolvers>();

    public void StartRouting()
    {
        _whenRouting?.TrySetResult();
        _whenRouting = null;
    }

    public bool IsBackendService(Type serviceType)
        => BackendServiceDefs.Contains(serviceType)
            || typeof(IBackendService).IsAssignableFrom(serviceType)
            || serviceType.Name.EndsWith("Backend", StringComparison.Ordinal);

    public RpcPeerRef RouteCall(RpcMethodDef methodDef, ArgumentList arguments)
    {
        var serviceDef = methodDef.Service;
        if (!serviceDef.IsBackend)
            throw StandardError.Internal("Only backend service methods can be called by servers.");

        var serverSideServiceDef = BackendServiceDefs[serviceDef.Type];
        var serviceMode = serverSideServiceDef.ServiceMode;
        if (serviceMode is not ServiceMode.Client and not ServiceMode.Distributed)
            throw StandardError.Internal($"{serviceDef} must be a ServiceMode.Client or ServiceMode.Distributed mode service.");

        if (_whenRouting is { Task.IsCompleted: false })
            return RpcPeerRef.Local;

        var meshRefResolver = RpcMeshRefResolvers[methodDef];
        var meshRef = meshRefResolver.Invoke(methodDef, arguments, serverSideServiceDef.ShardScheme);
        var peerRef = MeshWatcher.GetPeerRef(meshRef).Require(meshRef);
        if (serviceMode == ServiceMode.Distributed) {
            // Such services expose a client which may route calls to the server on the same node,
            // so the code below speeds up such calls by returning null RpcPeer for them, which makes
            // ActualLab.Rpc infrastructure to call the server method directly for them instead.
            switch (peerRef) {
            case RpcBackendNodePeerRef nodePeerRef when nodePeerRef.NodeRef == MeshNode.Ref:
                return RpcPeerRef.Local; // It's this node, so we won't forward this call
            case RpcBackendShardPeerRef { Latest.WhenReady: { IsCompletedSuccessfully: true } nodePeerRefTask }:
                if (nodePeerRefTask.Result is { } shardNodePeerRef && shardNodePeerRef.NodeRef == MeshNode.Ref)
                    return RpcPeerRef.Local; // It's this node, so we won't forward this call
                break;
            }
        }
        return peerRef;
    }

#pragma warning disable CA1822 // Can be static
    public Task<RpcConnection> GetConnection(
        RpcServerPeer peer, Channel<RpcMessage> channel, PropertyBag properties,
        CancellationToken cancellationToken)
#pragma warning restore CA1822
    {
        if (!properties.TryGet<HttpContext>(out var httpContext))
            return RpcConnectionTask(channel, properties);

        var session = httpContext.TryGetSessionFromHeader() ?? httpContext.TryGetSessionFromCookie();
        return session.IsValid()
            ? RpcBackendConnectionTask(channel, properties, session)
            : RpcConnectionTask(channel, properties);
    }

    private static Task<RpcConnection> RpcBackendConnectionTask(
        Channel<RpcMessage> channel, PropertyBag properties, Session session)
        => Task.FromResult<RpcConnection>(new RpcBackendConnection(channel, properties, session));

    private static Task<RpcConnection> RpcConnectionTask(
        Channel<RpcMessage> channel, PropertyBag options)
        => Task.FromResult(new RpcConnection(channel, options));
}
