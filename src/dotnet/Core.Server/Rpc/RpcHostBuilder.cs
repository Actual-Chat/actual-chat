using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualChat.Rpc.Internal;
using ActualLab.Fusion.Server;
using ActualLab.Fusion.Server.Middlewares;
using ActualLab.Fusion.Server.Rpc;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.Server;
using ActualLab.Rpc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Rpc;

[StructLayout(LayoutKind.Auto)]
public readonly struct RpcHostBuilder
{
    public FusionBuilder Fusion { get; }
    public IServiceCollection Services => Fusion.Services;
    public CommanderBuilder Commander => Fusion.Commander;
    public RpcBuilder Rpc => Fusion.Rpc;
    public HostInfo HostInfo { get; }
    public ILogger? Log { get; }

    public bool IsApiHost { get; }

    internal RpcHostBuilder(IServiceCollection services, HostInfo hostInfo, ILogger? log)
    {
        Fusion = services.AddFusion(RpcServiceMode.Local);
        HostInfo = hostInfo;
        Log = log;
        IsApiHost = HostInfo.HasRole(HostRole.Api);
        if (Services.HasService<BackendServiceDefs>())
            return; // Already configured

        if (Services.HasService<RpcWebSocketServer>())
            throw StandardError.Internal("Something is off: RpcWebSocketServer is already added.");

        // Common services
        if (IsApiHost)
            RpcFrameDelayers.DefaultProvider = RpcFrameDelayers.Auto(); // Only for API host!
        RpcServiceRegistry.ConstructionDumpLogLevel = LogLevel.Information;
        Services.AddSingleton(c => new BackendServiceDefs(c));
        Services.AddSingleton(c => new RpcMeshRefResolvers(c));
        Services.AddSingleton(c => new RpcBackendDelegates(c));
        AddMeshServices();
        AddRpcServer();
        AddRpcClient();
        AddRpcPeerFactory();

        // Debug stuff
        if (Constants.DebugMode.RpcCalls.AnyServerInboundDelay is { } delay)
            Rpc.AddInboundMiddleware(c => new RpcRandomDelayMiddleware(c) {
                Delay = delay,
            });
    }

    // AddApi

    public RpcHostBuilder AddApi<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>(Symbol name = default)
        where TService : class, IRpcService
        where TImplementation : class, TService
        => AddApiOrLocal(typeof(TService), typeof(TImplementation), false, name);

    public RpcHostBuilder AddApi(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType,
        Symbol name = default)
        => AddApiOrLocal(serviceType, implementationType, false, name);

    // AddApiOrLocal

    public RpcHostBuilder AddApiOrLocal<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>(
        Symbol name = default)
        where TService : class, IRpcService
        where TImplementation : class, TService
        => AddApiOrLocal(typeof(TService), typeof(TImplementation), true, name);

    public RpcHostBuilder AddApiOrLocal(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType,
        Symbol name = default)
        => AddApiOrLocal(serviceType, implementationType, true, name);

    private RpcHostBuilder AddApiOrLocal(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType,
        bool isLocalServiceRequired,
        Symbol name = default)
    {
        if (!typeof(IRpcService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IRpcService>(serviceType, nameof(serviceType));
        if (typeof(IBackendService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustNotImplement<IBackendService>(serviceType, nameof(serviceType));
        if (!serviceType.IsAssignableFrom(implementationType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo(implementationType, serviceType, nameof(implementationType));

        if (isLocalServiceRequired || IsApiHost)
            AddLocal(serviceType, implementationType);
        if (IsApiHost)
            Rpc.Service(serviceType).HasServer(serviceType).HasName(name);
        return this;
    }

    // AddBackend

    public RpcHostBuilder AddBackend<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>()
        where TService : class, IRpcService, IBackendService
        where TImplementation : class, TService
        => AddBackend(typeof(TService), typeof(TImplementation));

    public RpcHostBuilder AddBackend(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType,
        Symbol name = default)
    {
        if (!serviceType.IsInterface)
            throw ActualLab.Internal.Errors.MustBeInterface(serviceType, nameof(serviceType));
        if (!typeof(IRpcService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IRpcService>(serviceType, nameof(serviceType));
        if (!typeof(IBackendService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IBackendService>(serviceType, nameof(serviceType));
        if (!serviceType.IsAssignableFrom(implementationType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo(implementationType, serviceType, nameof(implementationType));

        var hostRoles = HostInfo.Roles;
        var serviceMode = hostRoles.GetBackendServiceMode(serviceType);
        if (!serviceMode.IsDisabled()) {
            var shardScheme = ShardScheme.ForType(serviceType) ?? ShardScheme.None;
            var serviceDef = new BackendServiceDef(serviceType, implementationType, serviceMode, shardScheme.HostRole);
            Services.Add(new ServiceDescriptor(typeof(BackendServiceDef), serviceDef));
        }

        switch (serviceMode) {
        case ServiceMode.Disabled:
            break;
        case ServiceMode.Local:
            AddLocal(serviceType, implementationType);
            break;
        case ServiceMode.Client:
            AddClient(serviceType, name);
            break;
        case ServiceMode.Server:
            AddServer(serviceType, implementationType, name);
            break;
        case ServiceMode.Distributed:
            AddDistributed(serviceType, implementationType, name);
            break;
        default:
            throw StandardError.Internal("Invalid ServiceMode value.");
        }
        return this;
    }

    // Private methods

    private void AddLocal(Type serviceType, Type implementationType)
    {
        if (typeof(IComputeService).IsAssignableFrom(serviceType))
            Fusion.AddComputeService(serviceType, implementationType, false);
        else
            Services.AddSingleton(serviceType, implementationType);
        Commander.AddHandlers(serviceType);
    }

    private void AddServer(Type serviceType, Type implementationType, Symbol name)
    {
        if (typeof(IComputeService).IsAssignableFrom(serviceType))
            Fusion.AddServer(serviceType, implementationType, name, false);
        else
            Rpc.AddServer(serviceType, implementationType, name);
        Commander.AddHandlers(serviceType);
    }

    private void AddClient(Type serviceType, Symbol name)
    {
        if (typeof(IComputeService).IsAssignableFrom(serviceType))
            Fusion.AddClient(serviceType, name, false);
        else
            Rpc.AddClient(serviceType, name);
        Commander.AddHandlers(serviceType);
    }

    private void AddDistributed(Type serviceType, Type implementationType, Symbol name)
    {
        if (typeof(IComputeService).IsAssignableFrom(serviceType))
            Fusion.AddDistributedService(serviceType, implementationType, name, false);
        else
            Rpc.AddDistributedService(serviceType, implementationType, name);
        Commander.AddHandlers(serviceType);
    }

    private void AddMeshServices()
    {
        var hostInfo = HostInfo;
        var log = Log;
        Services.AddSingleton<MeshNode>(c => {
            var host = Environment.GetEnvironmentVariable("POD_IP") ?? "";
            _ = int.TryParse(
                Environment.GetEnvironmentVariable("POD_PORT") ?? "80",
                CultureInfo.InvariantCulture,
                out var port);
            if (host.IsNullOrEmpty() || port == 0) {
                var endpoint = ServerEndpoints.List(c, "http://").FirstOrDefault();
                (host, port) = ServerEndpoints.Parse(endpoint);
                if (ServerEndpoints.InvalidHostNames.Contains(host)) {
                    if (hostInfo is { IsDevelopmentInstance: false, IsTested: false })
                        throw StandardError.Internal($"Server host name is invalid: {host}");

                    host = "localhost";
                    // host = Dns.GetHostName();
                }
            }

            var nodeId = new NodeRef(Generate.Option);
            var node = new MeshNode(
                nodeId, // $"{host}-{Ulid.NewUlid().ToString()}";
                $"{host}:{port.Format()}",
                hostInfo.Roles);
            log?.LogInformation("MeshNode: {MeshNode}", node.ToString());
            return node;
        });
        Services.AddSingleton(c => new MeshWatcher(c));
    }

    private void AddRpcServer()
    {
        Fusion.AddWebServer();

        // Replace RpcWebSocketServer.Options
        Services.AddSingleton(RpcWebSocketServer.Options.Default with {
            ExposeBackend = true,
            ConfigureWebSocket = () => new WebSocketAcceptContext() {
                DangerousEnableCompression = Constants.Api.Compression.IsServerSideEnabled,
            },
        });

        // Replace RpcBackendServiceDetector (it's used by both RPC client & server)
        Services.AddSingleton<RpcBackendServiceDetector>(c => c.GetRequiredService<RpcBackendDelegates>().IsBackendService);

        // Remove SessionMiddleware - we don't use it
        Services.RemoveAll<SessionMiddleware.Options>();
        Services.RemoveAll<SessionMiddleware>();

        // Replace DefaultSessionReplacerRpcMiddleware
        Rpc.RemoveInboundMiddleware<DefaultSessionReplacerRpcMiddleware>();
        Rpc.AddInboundMiddleware<RpcBackendDefaultSessionReplacerMiddleware>();

        // Replace RpcServerConnectionFactory
        Services.AddSingleton<RpcServerConnectionFactory>(c => c.GetRequiredService<RpcBackendDelegates>().GetConnection);
    }

    private void AddRpcClient()
    {
        Rpc.AddWebSocketClient();

        // Additional services
        Services.AddSingleton(c => new RpcMeshPeerRefCache(c));
        Services.AddSingleton(c => new RpcMeshRefResolvers(c));

        // Replace RpcCallRouter
        Services.AddSingleton<RpcCallRouter>(c => c.GetRequiredService<RpcBackendDelegates>().RouteCall);

        // Replace RpcWebSocketClient.Options
        Services.AddSingleton(c => RpcWebSocketClient.Options.Default with {
            ConnectionUriResolver = c.GetRequiredService<RpcBackendDelegates>().GetConnectionUri,
        });

        // Replace RpcClientPeerReconnectDelayer
        Services.AddSingleton(c => new RpcClientPeerReconnectDelayer(c) { Delays = RetryDelaySeq.Exp(1, 10) });
    }

    private void AddRpcPeerFactory()
    {
        var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
        var serverInboundCallLogLevel = Constants.DebugMode.RpcCalls.ApiServer && isDevelopmentInstance
            ? LogLevel.Debug
            : LogLevel.None;
        var backendInboundCallLogLevel = Constants.DebugMode.RpcCalls.BackendServer && isDevelopmentInstance
            ? LogLevel.Debug
            : LogLevel.None;
        var backendOutboundCallLogLevel = Constants.DebugMode.RpcCalls.BackendClient && isDevelopmentInstance
            ? LogLevel.Debug
            : LogLevel.None;
        Services.AddSingleton<RpcPeerFactory>(_
            => (hub, peerRef) => peerRef.IsServer
                ? new RpcServerPeer(hub, peerRef) {
                    CallLogLevel = peerRef.IsBackend ? backendInboundCallLogLevel : serverInboundCallLogLevel,
                }
                : new RpcClientPeer(hub, peerRef) {
                    CallLogLevel = backendOutboundCallLogLevel,
                });
    }
}
