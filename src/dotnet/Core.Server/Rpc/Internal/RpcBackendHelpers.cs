using ActualChat.Hosting;
using ActualLab.Caching;
using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Rpc.Internal;

#pragma warning disable VSTHRD002, VSTHRD104
#pragma warning disable CA1822 // Can be static

public sealed class RpcBackendHelpers(IServiceProvider services) : RpcServiceBase(services)
{
    private volatile TaskCompletionSource? _whenRoutingStarted = new();

    private MeshRpcPeerRefs PeerRefs { get; } = services.GetRequiredService<MeshRpcPeerRefs>();
    private BackendServiceDefs BackendServiceDefs { get; } = services.BackendServiceDefs();

    public void StartRouting()
    {
        _whenRoutingStarted?.TrySetResult();
        _whenRoutingStarted = null;
    }

    public Func<ArgumentList, RpcPeerRef> RouterFactory(RpcMethodDef methodDef)
    {
        var serviceDef = methodDef.Service;
        if (!serviceDef.IsBackend)
            return _ => throw StandardError.Internal("Only backend service methods can be called by servers.");

        var backendServiceDef = BackendServiceDefs[serviceDef.Type];
        if (backendServiceDef.ServiceMode is not ServiceMode.Client and not ServiceMode.Distributed)
            return _ => throw StandardError.Internal(
                $"{backendServiceDef} must be a ServiceMode.Client or Distributed mode service.");

        var typedRouter = GetTypedRouter(methodDef.Parameters.GetValueOrDefault(0)?.ParameterType);
        return args => {
            if (_whenRoutingStarted is { Task.IsCompleted: false })
                return RpcPeerRef.Local;

            var meshRef = typedRouter.Invoke(backendServiceDef, methodDef, args);
            var peerRef = PeerRefs.Get(meshRef).Require(meshRef);
            return peerRef;
        };
    }

    public Uri? GetConnectionUri(RpcClientPeer peer)
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

        var client = Services.GetRequiredService<RpcWebSocketClient>();
        var settings = client.Options;
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

    // Private methods

    private static Func<BackendServiceDef, RpcMethodDef, ArgumentList, MeshRef> GetTypedRouter(Type? arg0Type)
    {
        arg0Type ??= typeof(Unit);
        return GenericInstanceCache.Get<Func<BackendServiceDef, RpcMethodDef, ArgumentList, MeshRef>>(
            typeof(TypedRouterFactory<>),
            arg0Type);
    }

    // Nested types

    private sealed class TypedRouterFactory<T> : GenericInstanceFactory, IGenericInstanceFactory<T>
    {
        [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "We assume Task<T> methods are preserved")]
        public override Func<BackendServiceDef, RpcMethodDef, ArgumentList, MeshRef> Generate()
        {
            if (typeof(T) == typeof(Unit))
                return (backendServiceDef, _, _)
                    => MeshRef.ZeroShard.WithSchemeIfUndefined(backendServiceDef.ShardScheme);

            return (backendServiceDef, methodDef, args) => {
                var meshRef = MeshRefResolvers.Get<T>().Invoke(args.Get0<T>());
                return meshRef.WithSchemeIfUndefined(backendServiceDef.ShardScheme);
            };
        }
    }
}
