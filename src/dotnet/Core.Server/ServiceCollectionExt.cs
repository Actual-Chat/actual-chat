using System.Diagnostics.CodeAnalysis;
using ActualChat.Flows.Infrastructure;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Queues;
using ActualChat.Queues.InMemory;
using ActualChat.Queues.Internal;
using ActualChat.Queues.Nats;
using ActualChat.Rpc;
using ActualLab.CommandR.Internal;
using NATS.Client.Core;

namespace ActualChat;

public static class ServiceCollectionExt
{
    // Private accessors

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetImplementationType")]
    private static extern Type? ServiceDescriptorGetImplementationType(ServiceDescriptor @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_implementationFactory")]
    private static extern ref object ServiceDescriptorImplementationFactory(ServiceDescriptor @this);

    // HasService

    public static bool HasService<TService>(this IServiceCollection services, object serviceKey)
        => services.HasService(typeof(TService), serviceKey);
    public static bool HasService(this IServiceCollection services, Type serviceType, object serviceKey)
        => services.Any(d => d.ServiceType == serviceType && ReferenceEquals(d.ServiceKey, serviceKey));

    // AddRpcHost

    public static RpcHostBuilder AddRpcHost(
        this IServiceCollection services, HostInfo hostInfo, ILogger? log = null)
        => new(services, hostInfo, log);

    // AddFlows

    public static FlowRegistryBuilder AddFlows(
        this IServiceCollection services)
    {
        var flows = services.FindInstance<FlowRegistryBuilder>();
        if (flows != null)
            return flows;

        flows = new FlowRegistryBuilder();
        services.AddInstance(flows, addInFront: true);
        return flows;
    }

    // AddNats

    public static IServiceCollection AddNats(this IServiceCollection services, HostInfo hostInfo)
    {
        if (services.HasService<NatsSettings>())
            return services;

        // The code below is based on services.AddNats from NATS

        // NatsSettings
        services.AddSingleton(c => {
            var settings = c.Configuration().Settings<NatsSettings>();
            var instance = c.GetRequiredService<CoreSettings>().Instance;
            var instancePrefix = instance.IsNullOrEmpty() ? "" : instance + "-";
            settings = MemberwiseCloner.Invoke(settings);
            settings.InstancePrefix = instancePrefix;
            var log = c.LogFor<NatsSettings>();
            log.LogInformation("Using NATS, instance prefix: {InstancePrefix}", instancePrefix);
            return settings;
        });

        // NatsOpts
        services.AddSingleton(c => {
            var natsSettings = c.GetRequiredService<NatsSettings>();
            var natsTimeout = TimeSpan.FromSeconds(hostInfo.IsDevelopmentInstance ? 300 : 10);
            var options = NatsOpts.Default;
            return options with {
                Url = natsSettings.Url,
                TlsOpts = new NatsTlsOpts { Mode = TlsMode.Auto, },
                AuthOpts = natsSettings.Seed.IsNullOrEmpty() || natsSettings.NKey.IsNullOrEmpty()
                    ? NatsAuthOpts.Default
                    : new NatsAuthOpts { Seed = natsSettings.Seed, NKey = natsSettings.NKey, },
                CommandTimeout = natsTimeout,
                ConnectTimeout = natsTimeout,
                RequestTimeout = natsTimeout,
                LoggerFactory = c.GetRequiredService<ILoggerFactory>(),
            };
        });

        // NatsConnectionPool
        services.AddSingleton(c => {
            var options = c.GetRequiredService<NatsOpts>();
            return new NatsConnectionPool(1, options, static _ => { });
        });
        services.AddAlias<INatsConnectionPool, NatsConnectionPool>();

        // NatsConnection
        services.AddTransient<NatsConnection>(static provider => {
            var pool = provider.GetRequiredService<NatsConnectionPool>();
            return (pool.GetConnection() as NatsConnection)!;
        });
        services.AddAlias<INatsConnection, NatsConnection>(ServiceLifetime.Transient);
        return services;
    }

    // AddXxxQueues

    [RequiresUnreferencedCode(UnreferencedCode.Commander)]
    public static IServiceCollection AddInMemoryQueues(
        this IServiceCollection services,
        Func<IServiceProvider, InMemoryQueues.Options>? optionsBuilder = null)
    {
        services.AddCommander();
        if (!services.HasService<IQueues>()) {
            services.AddSingleton<InMemoryQueues>(c => new InMemoryQueues(c.GetRequiredService<InMemoryQueues.Options>(), c));
            services.AddSingleton<IQueues>(c => c.GetRequiredService<InMemoryQueues>());
            services.AddHostedService(c => c.Queues());
            services.AddSingleton(c => new BackendServiceDefs(c));
            services.AddSingleton<IQueueRefResolver>(c => new QueueRefResolver(c));
        }
        services.AddSingleton(optionsBuilder ?? (static _ => new InMemoryQueues.Options()));
        return services;
    }

    [RequiresUnreferencedCode(UnreferencedCode.Commander)]
    public static IServiceCollection AddNatsQueues(
        this IServiceCollection services,
        Func<IServiceProvider, NatsQueues.Options>? optionsBuilder = null)
    {
        services.AddCommander();
        if (!services.HasService<IQueues>()) {
            services.AddSingleton<NatsQueues>(c => new NatsQueues(c.GetRequiredService<NatsQueues.Options>(), c));
            services.AddSingleton<IQueues>(c => c.GetRequiredService<NatsQueues>());
            services.AddHostedService(c => c.Queues());
            services.AddSingleton(c => new BackendServiceDefs(c));
            services.AddSingleton<IQueueRefResolver>(c => new QueueRefResolver(c));
        }
        services.AddSingleton(optionsBuilder ?? (static _ => new NatsQueues.Options()));
        return services;
    }
}
