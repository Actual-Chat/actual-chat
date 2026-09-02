using System.Net.WebSockets;
using ActualChat.Resilience.Internal;
using ActualChat.Rpc.Internal;
using ActualLab.Fusion.Server;
using ActualLab.Fusion.Server.Middlewares;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.Middlewares;
using ActualLab.Rpc.Server;
using ActualLab.Rpc.WebSockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Rpc;

[StructLayout(LayoutKind.Auto)]
public readonly struct RpcHostBuilder
{
    private static readonly RpcWebSocketServerOriginValidator AppWebViewOriginValidator =
        RpcWebSocketServerOriginValidators.Allow(Constants.Origins.AppWebView);
    private static readonly RpcWebSocketServerOriginValidator AppOriginValidator =
        (server, context, origin) =>
            RpcWebSocketServerOriginValidators.SameOrigin.Invoke(server, context, origin)
            || AppWebViewOriginValidator.Invoke(server, context, origin);

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
            RpcWebSocketClientOptions.Default = RpcWebSocketClientOptions.Default with {
                UseAutoFrameDelayerFactory = true,
            };
        RpcServiceRegistry.ConstructionDumpLogLevel = LogLevel.Information;
        Services.AddSingleton(c => new BackendServiceDefs(c));
        Services.AddSingleton(c => new RpcBackendHelpers(c));
        AddMeshServices();
        AddRpcServer(IsApiHost);
        AddRpcClient();
        AddRpcPeerFactory();

        // Inbound call budgets
        if (IsApiHost)
            Rpc.AddMiddleware<RpcRateLimitMiddleware>();

        // Debug stuff
        if (CoreConstants.DebugMode.RpcCalls.AnyServerInboundDelay is { } delay)
            Rpc.AddMiddleware(_ => new RpcInboundCallDelayer() { Delay = delay });
    }

    // AddApi, AddLocalApi, AddBackend

    public RpcHostBuilder AddApi<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>(
        string name = "")
        where TService : class, IRpcService
        where TImplementation : class, TService
        => AddApi(typeof(TService), typeof(TImplementation), makeLocal: false, name);

    public RpcHostBuilder AddLocalApi<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>(
        string name = "")
        where TService : class, IRpcService
        where TImplementation : class, TService
        => AddApi(typeof(TService), typeof(TImplementation), makeLocal: true, name);

    public RpcHostBuilder AddBackend<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>(
        string name = "")
        where TService : class, IRpcService, IBackendService
        where TImplementation : class, TService
        => AddBackend(typeof(TService), typeof(TImplementation), name);

    // Private methods

    private RpcHostBuilder AddApi(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType,
        bool makeLocal,
        string name = "")
    {
        if (!typeof(IRpcService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IRpcService>(serviceType, nameof(serviceType));
        if (typeof(IBackendService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustNotImplement<IBackendService>(serviceType, nameof(serviceType));
        if (!serviceType.IsAssignableFrom(implementationType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo(
                implementationType, serviceType, nameof(implementationType));

        if (IsApiHost)
            AddServer(serviceType, implementationType, name);
        else if (makeLocal)
            AddLocal(serviceType, implementationType);
        return this;
    }

    private RpcHostBuilder AddBackend(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType,
        string name = "")
    {
        if (!serviceType.IsInterface)
            throw ActualLab.Internal.Errors.MustBeInterface(serviceType, nameof(serviceType));
        if (!typeof(IRpcService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IRpcService>(serviceType, nameof(serviceType));
        if (!typeof(IBackendService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IBackendService>(serviceType, nameof(serviceType));
        if (!serviceType.IsAssignableFrom(implementationType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo(
                implementationType, serviceType, nameof(implementationType));

        var hostRoles = HostInfo.Roles;
        var serviceMode = hostRoles.GetBackendServiceMode(serviceType);
        if (serviceMode is not ServiceMode.Disabled) {
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
            throw StandardError.Internal($"Invalid {nameof(ServiceMode)} value.");
        }
        return this;
    }

    private void AddLocal(Type serviceType, Type implementationType)
    {
        if (typeof(IComputeService).IsAssignableFrom(serviceType))
            Fusion.AddComputeService(serviceType, implementationType);
        else {
            Services.AddSingleton(serviceType, implementationType);
            Commander.AddHandlers(serviceType);
        }
    }

    private void AddServer(Type serviceType, Type implementationType, string name)
    {
        if (typeof(IComputeService).IsAssignableFrom(serviceType))
            Fusion.AddServer(serviceType, implementationType, name);
        else {
            Rpc.AddServer(serviceType, implementationType, name);
            Commander.AddHandlers(serviceType);
        }
    }

    private void AddClient(Type serviceType, string name)
    {
        if (typeof(IComputeService).IsAssignableFrom(serviceType))
            Fusion.AddClient(serviceType, name);
        else {
            Rpc.AddClient(serviceType, name);
            Commander.AddHandlers(serviceType);
        }
    }

    private void AddDistributed(Type serviceType, Type implementationType, string name)
    {
        if (typeof(IComputeService).IsAssignableFrom(serviceType))
            Fusion.AddDistributedService(serviceType, implementationType, name);
        else {
            Rpc.AddDistributedService(serviceType, implementationType, name);
            Commander.AddHandlers(serviceType);
        }
    }

    private void AddMeshServices()
    {
        var hostInfo = HostInfo;
        var log = Log;
        Services.AddSingleton<MeshNode>(c => {
            var host = Environment.GetEnvironmentVariable("POD_IP") ?? "";
            _ = int.TryParse(
                Environment.GetEnvironmentVariable("POD_PORT") ?? "80",
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
                hostInfo.Roles,
                MeshNodeState.Online);
            log?.LogInformation("MeshNode: {MeshNode}", node.ToString());
            return node;
        });
        Services.AddSingleton(c => new MeshWatcher(c));
    }

    private void AddRpcServer(bool isApiHost)
    {
        Fusion.AddWebServer();
        Rpc.AddHttpServer(exposeBackend: true);

        // Replace RpcWebSocketServerOptions
        Services.ReplaceFactory<RpcWebSocketServerOptions>((_, oldFactory) => oldFactory.Invoke() with {
            ExposeBackend = true,
            // The WebSocket handshake is exempt from CORS, and GetServerConnection resolves the
            // session from the cookie - so without a check here any page on any origin could open
            // an RPC connection carrying the visitor's session (cross-site WebSocket hijacking).
            // SameOrigin compares Origin against this request's own Host, so every hostname we
            // serve passes without being listed - voxt.ai, actual.chat, the dev/local ones and the
            // worktree subdomains alike. Non-browser clients send no Origin and are unaffected.
            // The MAUI app's WebView is the one client that's genuinely cross-origin: its host
            // page comes from Constants.Origins.AppWebView, never from the server's own host.
            OriginValidator = AppOriginValidator,
            // Media stream connections (any "kind" in the query - e.g. kind=video) opt out of
            // compression: their payloads are already compressed, and excluding them keeps
            // deflate from wasting CPU on them. DisableServerContextTakeover resets the deflate
            // context per message, so cross-message compression oracles (BREACH-style) don't apply.
            ConfigureWebSocket = (server, context, rpcRef) => new WebSocketAcceptContext() {
                DangerousEnableCompression = isApiHost
                    && Constants.Rpc.Compression.IsServerSideEnabled
                    && !context.Request.Query.ContainsKey("kind"),
                DisableServerContextTakeover = true,
            },
        });

        // Replace RpcRegistryOptions
        Services.ReplaceFactory<RpcRegistryOptions>((c, oldFactory) => {
            var oldOptions = oldFactory.Invoke();
            var oldServiceDefFactory = oldOptions.ServiceDefFactory;
            var isBackendSetter = typeof(RpcServiceDef)
                .GetProperty(nameof(RpcServiceDef.IsBackend))!
                .GetSetter<bool>();
            var backendServiceDefs = (BackendServiceDefs?)null;
            return oldOptions with {
                ServiceDefFactory = (hub, service) => {
                    backendServiceDefs ??= c.GetRequiredService<BackendServiceDefs>();
                    var serviceDef = oldServiceDefFactory.Invoke(hub, service);
                    isBackendSetter.Invoke(serviceDef, backendServiceDefs.Contains(service.Type));
                    return serviceDef;
                },
            };
        });

        // Remove SessionMiddleware - we don't use it
        Services.RemoveAll<SessionMiddleware.Options>();
        Services.RemoveAll<SessionMiddleware>();

        // Replace ServerConnectionFactory in RpcPeerOptions
        Services.ReplaceFactory<RpcPeerOptions>((c, oldFactory) => {
            var helpers = c.GetRequiredService<RpcBackendHelpers>();
            return oldFactory.Invoke() with {
                ServerConnectionFactory = helpers.GetServerConnection,
            };
        });
    }

    private void AddRpcClient()
    {
        Rpc.AddWebSocketClient();

        // Additional services
        Services.AddSingleton(c => new MeshRpcRefs(c));

        // Replace RpcOutboundCallOptions
        Services.ReplaceFactory<RpcOutboundCallOptions>((c, oldFactory) => {
            var helpers = c.GetRequiredService<RpcBackendHelpers>();
            return oldFactory.Invoke() with {
                RouterFactory = helpers.RouterFactory,
            };
        });

        // Replace RpcWebSocketClientOptions
        var isApiHost = IsApiHost; // Can't use ApiHost directly in the lambda below
        Services.ReplaceFactory<RpcWebSocketClientOptions>((c, oldFactory) => {
            var helpers = c.GetRequiredService<RpcBackendHelpers>();
            return oldFactory.Invoke() with {
                ConnectionUriResolver = helpers.GetConnectionUri,
                UseAutoFrameDelayerFactory = isApiHost, // Only for API host!
                WebSocketOwnerFactory = static peer => {
                    var ws = new ClientWebSocket();
                    // Explicitly disable permessage-deflate for backend RPC connections
                    ws.Options.DangerousDeflateOptions = null;
                    return new WebSocketOwner(peer.Ref.ToString(), ws, peer.Hub.Services);
                },
            };
        });

        // Replace RpcClientPeerReconnectDelayer: a backend peer typically fails to connect to a node
        // that's a second away from listening or from being rerouted away, so it must retry fast
        Services.AddSingleton(c => new RpcClientPeerReconnectDelayer(c) { Delays = RetryDelaySeq.Exp(0.25, 3) });
    }

    private void AddRpcPeerFactory()
    {
        var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
        var serverInboundCallLogLevel = CoreConstants.DebugMode.RpcCalls.ApiServer && isDevelopmentInstance
            ? LogLevel.Debug
            : LogLevel.None;
        var backendInboundCallLogLevel = CoreConstants.DebugMode.RpcCalls.BackendServer && isDevelopmentInstance
            ? LogLevel.Debug
            : LogLevel.None;
        var backendOutboundCallLogLevel = CoreConstants.DebugMode.RpcCalls.BackendClient && isDevelopmentInstance
            ? LogLevel.Debug
            : LogLevel.None;
        Services.ReplaceFactory<RpcPeerOptions>((_, oldFactory) => oldFactory.Invoke() with {
            PeerFactory = (hub, route) => route.Ref.IsServer
                ? new RpcServerPeer(hub, route) {
                    CallLogLevel = route.Ref.IsBackend ? backendInboundCallLogLevel : serverInboundCallLogLevel,
                }
                : new RpcClientPeer(hub, route) {
                    CallLogLevel = backendOutboundCallLogLevel,
                }
        });
    }
}
