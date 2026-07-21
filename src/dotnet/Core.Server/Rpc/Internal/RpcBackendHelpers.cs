using ActualChat.Hosting;
using ActualLab.Caching;
using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Rpc.Internal;

#pragma warning disable VSTHRD104
#pragma warning disable CA1822 // Can be static

public sealed class RpcBackendHelpers(IServiceProvider services) : RpcServiceBase(services)
{
    private volatile TaskCompletionSource? _whenRoutingStarted = TaskCompletionSourceExt.New();

    private MeshRpcRefs RpcRefs { get; } = services.GetRequiredService<MeshRpcRefs>();
    private BackendServiceDefs BackendServiceDefs { get; } = services.BackendServiceDefs();

    public void StartRouting()
    {
        _whenRoutingStarted?.TrySetResult();
        _whenRoutingStarted = null;
    }

    public Func<ArgumentList, RpcRef> RouterFactory(RpcMethodDef methodDef)
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
                return RpcRef.Local;

            var meshRef = typedRouter.Invoke(backendServiceDef, methodDef, args);
            var rpcRef = RpcRefs.Get(meshRef).Require(meshRef);
            return rpcRef;
        };
    }

    public Uri? GetConnectionUri(RpcClientPeer peer)
    {
        if (peer.Ref is not MeshRpcRef rpcRef)
            throw new RpcReconnectFailedException($"Unsupported RpcRef type: {peer.Ref}.");

        MeshNode? node;
        if (peer.Route is MeshRpcRoute route) {
            // Shard-based refs can be rerouted; the peer must keep connecting to the target
            // of its own route generation. If its node is dead or doesn't exist,
            // we must await for rerouting.
            node = route.Resolved.Node;
            if (node is null || node.State is MeshNodeState.Dead)
                return null; // null Uri = the peer will wait for Route.ChangedToken cancellation and reconnect
        }
        else {
            // Node refs use a static route, so they can't be rerouted.
            // But since nodes can die, we must handle this scenario here.
            node = rpcRef.Resolved.Node; // Re-resolved on each access (to see the current state)
            if (node is null || node.State is MeshNodeState.Dead) // The latest node is dead
                throw new RpcReconnectFailedException($"Node {rpcRef.NodeRef} is dead."); // Terminate the peer
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
        RpcServerPeer peer, RpcTransport transport, PropertyBag properties,
        CancellationToken cancellationToken)
    {
        if (!properties.KeylessTryGet<HttpContext>(out var httpContext))
            return Task.FromResult(new RpcConnection(transport, properties));

        var session = httpContext.TryGetSessionFromHeader()
            ?? httpContext.TryGetSessionFromQuery()
            ?? httpContext.TryGetSessionFromCookie();
        return Task.FromResult(session.IsValid()
            ? new RpcBackendConnection(transport, properties, session)
            : new RpcConnection(transport, properties));
    }

    // Private methods

    private static Func<BackendServiceDef, RpcMethodDef, ArgumentList, MeshRef> GetTypedRouter(Type? arg0Type)
    {
        arg0Type ??= typeof(Unit);
        return GenericInstanceCache.GetUnsafe<Func<BackendServiceDef, RpcMethodDef, ArgumentList, MeshRef>>(
            typeof(TypedRouterFactory<>),
            arg0Type);
    }

    // Nested types

    private sealed class TypedRouterFactory<T> : GenericInstanceFactory, IGenericInstanceFactory<T>
    {
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
