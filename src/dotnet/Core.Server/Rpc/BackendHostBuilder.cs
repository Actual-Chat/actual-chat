using ActualChat.Hosting;
using ActualLab.Fusion.Server;
using ActualLab.Fusion.Server.Middlewares;
using ActualLab.Fusion.Server.Rpc;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Rpc;

[StructLayout(LayoutKind.Auto)]
public readonly struct BackendHostBuilder
{
    public FusionBuilder Fusion { get; }
    public IServiceCollection Services => Fusion.Services;
    public CommanderBuilder Commander => Fusion.Commander;
    public RpcBuilder Rpc => Fusion.Rpc;
    public HostInfo HostInfo { get; }
    public ILogger? Log { get; }

    internal BackendHostBuilder(IServiceCollection services, HostInfo hostInfo, ILogger? log)
    {
        Fusion = services.AddFusion(RpcServiceMode.None);
        HostInfo = hostInfo;
        Log = log;

        if (services.HasService<BackendServiceDefs>())
            throw StandardError.Internal("Something is off: BackendHostBuilder can be called just once.");
        if (services.HasService<RpcWebSocketServer>())
            throw StandardError.Internal("Something is off: RpcWebSocketServer is already added.");
        if (services.HasService<RpcClient>())
            throw StandardError.Internal("Something is off: RpcClient is already added.");

        // Common services
        Services.AddMeshWatcher(HostInfo, Log);
        Services.AddSingleton(c => new BackendServiceDefs(c));
        Services.AddSingleton(c => new RpcMeshRefResolvers(c));
        Services.AddSingleton(c => new RpcBackendDelegates(c));

        // Common services
        Fusion.AddWebServer();
        SetupServer();
        SetupClient();
    }

    // Private methods

    private void SetupServer()
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

    public void SetupClient()
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
        Services.AddSingleton(c => new RpcClientPeerReconnectDelayer(c) { Delays = RetryDelaySeq.Exp(0.25, 10) });
    }
}
