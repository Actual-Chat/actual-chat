using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Rpc;
using ActualChat.Security;
using ActualChat.UI;
using Stl.Fusion.Extensions;
using Stl.Fusion.Internal;
using Stl.Generators;
using Stl.Mathematics.Internal;
using Stl.Rpc;

namespace ActualChat.Module;

#pragma warning disable IL2026, IL2111 // Fine for modules
#pragma warning disable CA1822 // Method can be static

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class CoreModule : HostModule<CoreSettings>
{
    static CoreModule()
    {
        // This type initializer sets all super-early defaults

        // Session.Factory & Validator
#pragma warning disable CA2000
        Session.Factory = DefaultSessionFactory.New(new RandomStringGenerator(20, Alphabet.AlphaNumericDash.Symbols));
#pragma warning restore CA2000
        Session.Validator = session => session.Id.Value.Length >= 20;

#if false
        // Default binary serializer
        ByteSerializer.Default = MessagePackByteSerializer.Default;

        // Default caching settings
        ComputedOptions.ClientDefault = ComputedOptions.ClientDefault with {
            ClientCacheMode = ClientCacheMode.NoCache,
        };
#endif
    }

    public CoreModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected internal override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        var appKind = HostInfo.AppKind;
        var isServer = appKind.IsServer();
        var isClient = appKind.IsClient();

        // Common services
        services.AddTracer();
        services.AddSingleton(c => new StaticImportsInitializer(c));
        services.AddHostedService(c => c.GetRequiredService<StaticImportsInitializer>());
        services.AddSingleton(c => new UrlMapper(c.GetRequiredService<HostInfo>()));
        services.AddSingleton(c => new HealthEventListener(c));
        services.AddSingleton<IRuntimeStats>(c => c.GetRequiredService<HealthEventListener>());

        // IArithmetics
        services.AddTypeMapper<IArithmetics>(map => map
            .Add<double, DoubleArithmetics>()
            .Add<int, IntArithmetics>()
            .Add<long, LongArithmetics>()
            .Add<Moment, MomentArithmetics>()
            .Add<TimeSpan, TimeSpanArithmetics>()
        );

        // IDiffHandlers
        services.AddTypeMapper<IDiffHandler>(DiffEngine.DefaultTypeMapBuilder);

        // DiffEngine
        services.AddSingleton(c => new DiffEngine(c));

        // Fusion
        var fusion = services.AddFusion();
        if (isServer) {
            // It's quite important to make sure fusion.WithServiceMode call follows the very first
            // services.AddFusion call, otherwise every fusion.AddService(...) call that happens earlier
            // won't be affected by this mode change!
            fusion = fusion.WithServiceMode(RpcServiceMode.Server, true);
        }
        else if (isClient) {
            services.AddScoped<ISessionResolver>(c => new DefaultSessionResolver(c));
            if (appKind.IsMauiApp())
                services.AddSingleton(c => new TrueSessionResolver(c));

            services.AddSingleton(c => new RpcClientPeerReconnectDelayer(c) {
                Delays = RetryDelaySeq.Exp(1, 180),
            });
            if (appKind.IsWasmApp() && HostInfo.IsDevelopmentInstance) {
                if (Constants.DebugMode.RpcClient)
                    services.AddSingleton<RpcPeerFactory>(_
                        => static (hub, peerRef) => peerRef.IsServer
                            ? throw StandardError.NotSupported("No server peers on the client.")
                            : new RpcClientPeer(hub, peerRef) { CallLogLevel = LogLevel.Debug });
            }
            else
                RpcServiceRegistry.ConstructionDumpLogLevel = LogLevel.None;
        }
        fusion.AddComputedGraphPruner(_ => new ComputedGraphPruner.Options() {
            CheckPeriod = TimeSpan.FromMinutes(isClient || IsDevelopmentInstance ? 5 : 10).ToRandom(0.1),
        });
        fusion.AddFusionTime();

        // Features
        services.AddScoped(c => new Features(c));
        fusion.AddService<IClientFeatures, ClientFeatures>(ServiceLifetime.Scoped);

        // UI
        services.AddScoped(_ => new SystemSettingsUI());

        if (isServer)
            InjectServerServices(services);
        if (isClient)
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

        // Reconnector
        services.AddSingleton(c => new RpcDependentReconnectDelayer(c));

        // Features
        fusion.AddClient<IServerFeaturesClient>();
        fusion.Rpc.Service<IServerFeaturesClient>().HasName(nameof(IServerFeatures));
        fusion.AddService<IServerFeatures, ServerFeaturesClient>();
    }
}
