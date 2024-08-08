using ActualChat.Hosting;
using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Rpc.Internal;

#pragma warning disable VSTHRD002, VSTHRD104
#pragma warning disable CA1822 // Can be static

public sealed class RpcBackendDelegates(IServiceProvider services) : RpcServiceBase(services)
{
    private volatile TaskCompletionSource? _whenRoutingStarted = new();

    private RpcMeshPeerRefCache MeshPeerRefs { get; } = services.GetRequiredService<RpcMeshPeerRefCache>();
    private RpcMeshRefResolvers RpcMeshRefResolvers { get; } = services.GetRequiredService<RpcMeshRefResolvers>();
    private BackendServiceDefs BackendServiceDefs { get; } = services.GetRequiredService<BackendServiceDefs>();

    public void StartRouting()
    {
        _whenRoutingStarted?.TrySetResult();
        _whenRoutingStarted = null;
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

        if (_whenRoutingStarted is { Task.IsCompleted: false })
            return RpcPeerRef.Local;

        var meshRefResolver = RpcMeshRefResolvers[methodDef];
        var meshRef = meshRefResolver.Invoke(methodDef, arguments, serverSideServiceDef.ShardScheme);
        var peerRef = MeshPeerRefs.Get(meshRef).Require(meshRef);
        return peerRef;
    }

    public Uri? GetConnectionUri(RpcWebSocketClient client, RpcClientPeer peer)
    {
        if (peer.Ref is not RpcMeshPeerRef meshPeerRef)
            return null; // This causes RPC connection to hang waiting for RpcPeerRef.RerouteToken cancellation

        var target = meshPeerRef.Target;
        if (target.Node is not { } node)
            return null; // This causes RPC connection to hang waiting for RpcPeerRef.RerouteToken cancellation

        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append("ws://");
        sb.Append(node.Endpoint);
        sb.Append(client.Settings.BackendRequestPath);
        sb.Append('?');
        sb.Append(client.Settings.ClientIdParameterName);
        sb.Append('=');
        sb.Append(peer.ClientId); // Always Url-encoded
        return sb.ToStringAndRelease().ToUri();
    }

    public Task<RpcConnection> GetConnection(
        RpcServerPeer peer, Channel<RpcMessage> channel, PropertyBag properties,
        CancellationToken cancellationToken)
    {
        if (!properties.TryGet<HttpContext>(out var httpContext))
            return Task.FromResult(new RpcConnection(channel, properties));

        var session = httpContext.TryGetSessionFromHeader() ?? httpContext.TryGetSessionFromCookie();
        return Task.FromResult(session.IsValid()
            ? new RpcBackendConnection(channel, properties, session)
            : new RpcConnection(channel, properties));
    }
}
