using ActualChat.Mesh;
using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Rpc;

public sealed class RpcBackendDelegates(IServiceProvider services) : RpcServiceBase(services)
{
    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private BackendServiceDefs BackendServiceDefs { get; } = services.GetRequiredService<BackendServiceDefs>();
    private RpcMeshRefResolvers RpcMeshRefResolvers { get; } = services.GetRequiredService<RpcMeshRefResolvers>();

    public bool IsBackendService(Type serviceType, Symbol serviceName)
        => BackendServiceDefs.Contains(serviceType)
            || typeof(IBackendService).IsAssignableFrom(serviceType)
            || serviceType.Name.EndsWith("Backend", StringComparison.Ordinal)
            || serviceName.Value.StartsWith("backend.", StringComparison.Ordinal);

    public RpcPeer GetPeer(RpcMethodDef methodDef, ArgumentList arguments)
    {
        var serviceDef = methodDef.Service;
        if (!serviceDef.IsBackend)
            throw StandardError.Internal("Only backend service methods can be called by servers.");

        var serverSideServiceDef = BackendServiceDefs[serviceDef.Type];
        if (serverSideServiceDef.ServiceMode != ServiceMode.Client)
            throw StandardError.Internal($"{serviceDef} must be a ServiceMode.Client service.");

        var shardScheme = ShardScheme.ById[serverSideServiceDef.ServedByRoles.Backend.Id];
        var meshRefResolver = RpcMeshRefResolvers[methodDef];
        var meshRef = meshRefResolver.Invoke(methodDef, arguments, shardScheme);
        var peerRef = MeshWatcher.GetPeerRef(meshRef).Require();
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
