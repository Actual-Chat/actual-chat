using ActualChat.Hosting;
using ActualChat.Mesh;
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

    private MeshRpcPeerRefs PeerRefs { get; } = services.GetRequiredService<MeshRpcPeerRefs>();
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
        // When invalidation is active, commands must be routed to the local peer to handle it locally
        if (methodDef.IsCommand && Invalidation.IsActive)
            return RpcPeerRef.Local;

        var serviceDef = methodDef.Service;
        if (!serviceDef.IsBackend)
            throw StandardError.Internal("Only backend service methods can be called by servers.");

        var serverSideServiceDef = BackendServiceDefs[serviceDef.Type];
        var serviceMode = serverSideServiceDef.ServiceMode;
        if (serviceMode is not ServiceMode.Client and not ServiceMode.Distributed)
            throw StandardError.Internal($"{serviceDef} must be a ServiceMode.Client or Distributed mode service.");

        if (_whenRoutingStarted is { Task.IsCompleted: false })
            return RpcPeerRef.Local;

        var callRouter = methodDef.GetCallRouter();
        var meshRef = callRouter.Invoke(methodDef, arguments, serverSideServiceDef.ShardScheme);
        var peerRef = PeerRefs.Get(meshRef).Require(meshRef);
        return peerRef;
    }

    public Uri? GetConnectionUri(RpcWebSocketClient client, RpcClientPeer peer)
    {
        if (peer.Ref is not MeshRpcPeerRef peerRef)
            throw new RpcReconnectFailedException($"Unsupported RpcPeerRef type: {peer.Ref}.");

        var target = peerRef.Target;
        var node = target.Node;
        if (node is null) {
            // No node -> target.State is Unknown or Dead
            if (target.ShardRef.IsNone && target.Node?.State == MeshNodeState.Dead) // Such targets are never rerouted
                throw new RpcReconnectFailedException($"Node {target.NodeRef} is dead."); // Makes peer to terminate

            return null; // null Uri = peer will hang waiting for RpcPeerRef.RerouteToken cancellation
        }

        var settings = client.Settings;
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append("ws://");
        sb.Append(node.Endpoint);
        sb.Append(settings.BackendRequestPath);
        sb.Append('?');
        sb.Append(settings.ClientIdParameterName);
        sb.Append('=');
        sb.Append(peer.ClientId); // Always Url-encoded
        sb.Append('&');
        sb.Append(settings.SerializationFormatParameterName);
        sb.Append('=');
        sb.Append(peer.SerializationFormat.Key);
        return sb.ToStringAndRelease().ToUri();
    }

    public Task<RpcConnection> GetServerConnection(
        RpcServerPeer peer, Channel<RpcMessage> channel, PropertyBag properties,
        CancellationToken cancellationToken)
    {
        if (!properties.KeylessTryGet<HttpContext>(out var httpContext))
            return Task.FromResult(new RpcConnection(channel, properties));

        var session = httpContext.TryGetSessionFromHeader() ?? httpContext.TryGetSessionFromCookie();
        return Task.FromResult(session.IsValid()
            ? new RpcBackendConnection(channel, properties, session)
            : new RpcConnection(channel, properties));
    }
}
