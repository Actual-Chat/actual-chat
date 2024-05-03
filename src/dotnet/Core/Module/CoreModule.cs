using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Rpc;
using ActualChat.Security;
using ActualChat.UI;
using ActualLab.Fusion.Extensions;
using ActualLab.Fusion.Internal;
using ActualLab.Generators;
using ActualLab.Mathematics.Internal;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Module;

#pragma warning disable IL2026, IL2111 // Fine for modules
#pragma warning disable CA1822 // Method can be static

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class CoreModule(IServiceProvider moduleServices)
    : HostModule<CoreSettings>(moduleServices)
{
    static CoreModule()
    {
        // This type initializer sets all super-early defaults

        // Rpc - API version
        RpcDefaults.ApiVersion = RpcDefaults.BackendVersion = Constants.Api.Version;

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

        // Overrides default requirements for User type
        User.MustExist = Requirement.New(
            new(() => StandardError.Account.Guest()),
            (User? u) => u != null);
        User.MustBeAuthenticated = Requirement.New(
            new(() => StandardError.Account.Guest()),
            (User? u) => u?.IsAuthenticated() == true);

        // Any AccountException isn't a transient error
        var oldPreferTransient = TransiencyResolvers.PreferTransient;
        TransiencyResolvers.PreferTransient = e => {
            var transiency = oldPreferTransient.Invoke(e);
            if (!transiency.IsTransient())
                return transiency;

            return e switch {
                AccountException => Transiency.NonTransient,
                _ => transiency,
            };
        };
    }

    protected internal override void InjectServices(IServiceCollection services)
    {
        var hostKind = HostInfo.HostKind;
        var isApp = hostKind.IsApp();
        var isServer = hostKind.IsServer();

        // Common services
        services.AddSingleton(c => new StaticImportsInitializer(c));
        services.AddHostedService(c => c.GetRequiredService<StaticImportsInitializer>());
        services.AddSingleton(c => new UrlMapper(c.HostInfo()));
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
        else if (isApp) {
            services.AddScoped<ISessionResolver>(c => new DefaultSessionResolver(c));
            if (hostKind.IsMauiApp())
                services.AddSingleton(c => new TrueSessionResolver(c));

            services.AddSingleton(c => new RpcClientPeerReconnectDelayer(c) {
                Delays = RetryDelaySeq.Exp(1, 180),
            });

            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var rpcCallLogLevel = Constants.DebugMode.RpcCalls.ApiClient && isDevelopmentInstance
                ? LogLevel.Debug
                : LogLevel.None;
            services.AddSingleton<RpcPeerFactory>(_
                => (hub, peerRef) => peerRef.IsServer
                    ? throw StandardError.Internal("Server peer is requested on the client side!")
                    : new RpcClientPeer(hub, peerRef) { CallLogLevel = rpcCallLogLevel });

            RpcServiceRegistry.ConstructionDumpLogLevel = hostKind.IsWasmApp() && isDevelopmentInstance
                ? LogLevel.Debug
                : LogLevel.None;
        }
        fusion.AddComputedGraphPruner(_ => new ComputedGraphPruner.Options() {
            CheckPeriod = TimeSpan.FromMinutes(isApp || HostInfo.IsDevelopmentInstance ? 5 : 10).ToRandom(0.1),
        });
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

        // Reconnector
        services.AddSingleton(c => new RpcDependentReconnectDelayer(c));

        // Features
        fusion.AddClient<IServerFeaturesClient>();
        fusion.Rpc.Service<IServerFeaturesClient>().HasName(nameof(IServerFeatures));
        fusion.AddService<IServerFeatures, ServerFeaturesClient>();
    }
}
