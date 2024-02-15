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

    internal RpcHostBuilder(IServiceCollection services, HostInfo hostInfo, ILogger? log)
    {
        Fusion = services.AddFusion(RpcServiceMode.None);
        HostInfo = hostInfo;
        Log = log;

        if (!Services.HasService<BackendServiceDefs>())
            AddCoreServices();
    }

    // GetServiceMode

    public ServiceMode GetServiceMode<TService>()
        => GetServiceMode(typeof(TService));
    public ServiceMode GetServiceMode(Type serviceType)
    {
        var servedByRoles = HostRoles.GetServedByRoles(serviceType);
        return HostInfo.GetServiceMode(servedByRoles);
    }

    // AddFrontend & AddBackend auto-detect IComputeService & IRpcService

    public RpcHostBuilder AddFrontend<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>()
        where TService : class, IRpcService
        where TImplementation : class, TService
        => AddFrontend(typeof(TService), typeof(TImplementation));

    public RpcHostBuilder AddFrontend(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType,
        Symbol name = default)
    {
        if (!HostInfo.HasRole(HostRole.FrontendServer))
            return this;

        if (!typeof(IRpcService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IRpcService>(serviceType, nameof(serviceType));
        if (typeof(IBackendService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustNotImplement<IBackendService>(serviceType, nameof(serviceType));
        if (!serviceType.IsAssignableFrom(implementationType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo(implementationType, serviceType, nameof(implementationType));

        var isComputeService = typeof(IComputeService).IsAssignableFrom(serviceType);
        if (isComputeService)
            Fusion.AddService(serviceType, implementationType);
        else
            Commander.AddService(serviceType, implementationType);
        return this;
    }

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
        if (!typeof(IRpcService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IRpcService>(serviceType, nameof(serviceType));
        if (!typeof(IBackendService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IBackendService>(serviceType, nameof(serviceType));
        if (!serviceType.IsAssignableFrom(implementationType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo(implementationType, serviceType, nameof(implementationType));

        var servedByRoles = new ServedByRoleSet(HostRoles.GetServedByRoles(serviceType));
        var serviceMode = HostInfo.GetServiceMode(servedByRoles.AllRoles);
        var serviceDef = new BackendServiceDef(serviceType, implementationType, servedByRoles, serviceMode);
        Services.Add(new ServiceDescriptor(typeof(BackendServiceDef), serviceDef));

        var isComputeService = typeof(IComputeService).IsAssignableFrom(serviceType);
        switch (serviceMode) {
        case ServiceMode.SelfHosted:
            if (isComputeService)
                Fusion.AddService(serviceType, implementationType);
            else
                Commander.AddService(serviceType, implementationType);
            break;
        case ServiceMode.Server:
            if (isComputeService)
                Fusion.AddServer(serviceType, implementationType, name);
            else {
                Rpc.AddServer(serviceType, implementationType, name);
                Commander.AddHandlers(serviceType, implementationType);
            }
            break;
        case ServiceMode.Client:
            if (isComputeService)
                Fusion.AddClient(serviceType, name);
            else {
                Rpc.AddClient(serviceType, name);
                Commander.AddHandlers(serviceType);
            }
            break;
        default:
            throw StandardError.Internal("Invalid ServiceMode value.");
        }
        return this;
    }

    // Private methods

    private void AddCoreServices()
    {
        if (Services.HasService<RpcWebSocketServer>())
            throw StandardError.Internal("Something is off: RpcWebSocketServer is already added.");
        if (Services.HasService<RpcClient>())
            throw StandardError.Internal("Something is off: RpcClient is already added.");

        // Common services
        Services.AddSingleton(c => new BackendServiceDefs(c));
        Services.AddSingleton(c => new RpcMeshRefResolvers(c));
        Services.AddSingleton(c => new RpcBackendDelegates(c));
        AddMeshServices();
        Fusion.AddWebServer();
        AddRpcServer(); // Must follow AddWebServer
        AddRpcClient();
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
                    if (!hostInfo.IsDevelopmentInstance)
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
        // Replace
        Services.AddSingleton(RpcWebSocketServer.Options.Default with {
            ExposeBackend = !HostInfo.HasRole(HostRole.SingleServer),
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
        // Replace RpcCallRouter
        Services.AddSingleton<RpcCallRouter>(c => c.GetRequiredService<RpcBackendDelegates>().GetPeer);

        // Backend-only RpcClient
        Services.AddSingleton(_ => RpcWebSocketClient.Options.Default);
        Services.AddSingleton(c => {
            var options = c.GetRequiredService<RpcWebSocketClient.Options>();
            return new RpcBackendWebSocketClient(options, c);
        });
        Services.AddAlias<RpcClient, RpcBackendWebSocketClient>();

        // Replace RpcClientPeerReconnectDelayer
        Services.AddSingleton(c => new RpcClientPeerReconnectDelayer(c) { Delays = RetryDelaySeq.Exp(1, 10) });
    }
}
