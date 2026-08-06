using ActualChat.Security;
using ActualChat.UI;
using ActualLab.Fusion.Extensions;
using ActualLab.Fusion.Internal;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Module;

/// <summary>
/// Configures core services for both server and client applications.
/// </summary>
#pragma warning disable CA1822 // Method can be static

public sealed class CoreModule(IServiceProvider moduleServices)
    : HostModule<CoreSettings>(moduleServices)
{
    protected internal override void InjectServices(IServiceCollection services)
    {
        var hostKind = HostInfo.HostKind;
        var isApp = hostKind.IsApp();
        var isServer = hostKind.IsServer();

        // TestServiceProviderTag
        if (HostInfo.IsTested)
            services.AddSingleton(new TestServiceProviderTag());

        // IDiffHandlers
        services.AddTypeMapper<IDiffHandler>(DiffEngine.DefaultTypeMapBuilder);

        // DiffEngine
        services.AddSingleton(c => new DiffEngine(c));

        // Fusion
        ComputedGraphPruner.Settings.CheckPeriod
            = TimeSpan.FromMinutes(isApp || HostInfo.IsDevelopmentInstance ? 5 : 10).ToRandom(0.1);

        var fusion = services.AddFusion();
        // Overrides FusionBuilder's TryAddSingleton default (TransiencyResolvers.PreferTransient)
        services.AddTransiencyResolver<Computed>(_ => ComputedTransiencyResolver.Instance);
        if (isServer) {
            // It's quite important to make sure fusion.WithServiceMode call follows the very first
            // services.AddFusion call, otherwise every fusion.AddService(...) call that happens earlier
            // won't be affected by this mode change!
            fusion = fusion.WithServiceMode(RpcServiceMode.Server, true);
        }
        else if (isApp) {
            services.AddScoped<ISessionResolver>(c => new DefaultSessionResolver(c));
            if (hostKind.IsMauiApp())
                services.AddSingleton(c => new TrueSessionResolver(c));

            services.AddSingleton(c => new RpcClientPeerReconnectDelayer(c) {
                Delays = RetryDelaySeq.Exp(1, 180),
            });

            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var rpcCallLogLevel = CoreConstants.DebugMode.RpcCalls.ApiClient && isDevelopmentInstance
                ? LogLevel.Debug
                : LogLevel.None;
            services.ReplaceFactory<RpcPeerOptions>((_, oldFactory) => oldFactory.Invoke() with {
                PeerFactory = (hub, route) => route.Ref.IsServer
                    ? throw StandardError.Internal("Server peer is requested on the client side!")
                    : new RpcClientPeer(hub, route) {
                        CallLogLevel = rpcCallLogLevel,
                    },
            });

            RpcServiceRegistry.ConstructionDumpLogLevel = hostKind.IsWasmApp() && isDevelopmentInstance
                ? LogLevel.Debug
                : LogLevel.None;
        }
        fusion.AddFusionTime();

        // Features
        services.AddScoped(c => new Features(c));
        fusion.AddService<IClientFeatures, ClientFeatures>(ServiceLifetime.Scoped);

        // UI
        services.AddScoped(_ => new SystemSettingsUI());

        if (isServer)
            InjectServerServices(services);
        if (isApp)
            InjectClientServices(services);
    }

    private void InjectServerServices(IServiceCollection services)
    {
        var fusion = services.AddFusion();
        fusion.AddService<IServerFeatures, ServerFeatures>();
    }

    private void InjectClientServices(IServiceCollection services)
    {
        var fusion = services.AddFusion();

        // Features
        fusion.AddClient<IServerFeaturesClient>();
        fusion.Configure<IServerFeaturesClient>().HasName(nameof(IServerFeatures));
        fusion.AddService<IServerFeatures, ServerFeaturesClient>();
    }
}
