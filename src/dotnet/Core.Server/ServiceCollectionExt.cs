using ActualChat.Flows.Infrastructure;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Queues;
using ActualChat.Queues.InMemory;
using ActualChat.Queues.Internal;
using ActualChat.Queues.Nats;
using ActualChat.Rpc;
using NATS.Client.Core;

namespace ActualChat;

[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "Fine for server code")]
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

    public static FlowDefsBuilder AddFlows(
        this IServiceCollection services,
        bool? useMasterFlows = null)
    {
        var flowRegistryBuilder = services.FindOrAddInstance(() => new FlowDefsBuilder(), addInFront: true);
        if (useMasterFlows.HasValue)
            flowRegistryBuilder.UseMasterFlows = useMasterFlows.Value;
        return flowRegistryBuilder;
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
                WriterBufferSize = 655360, // NATS CommandWriter makes _arrayPoolInitialSize = opts.WriterBufferSize / 256;
            };
        });

        // NatsConnectionPool
        services.AddSingleton(c => {
            var options = c.GetRequiredService<NatsOpts>();
            var log = c.LogFor<NatsConnectionPool>();
            return new NatsConnectionPool(
                Math.Min(Environment.ProcessorCount / 2, 3),
                options,
                connection => {
                    connection.ConnectionOpened += (_, args) => {
                        log.LogInformation("Connection opened: {Message}", args.Message);
                        return ValueTask.CompletedTask;
                    };
                    connection.ConnectionDisconnected += (_, args) => {
                        log.LogInformation("Disconnected: {Message}", args.Message);
                        return ValueTask.CompletedTask;
                    };
                    connection.ReconnectFailed += (sender, args) => {
                        log.LogWarning("Reconnect failed: {Message}", args.Message);
                        return ValueTask.CompletedTask;
                    };
                    connection.MessageDropped += (sender, args) => {
                        log.LogError("Message dropped: {Message}, {Subject}", args.Message, args.Subject);
                        return ValueTask.CompletedTask;
                    };
                });
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
        }
        services.AddSingleton(optionsBuilder ?? (static _ => new InMemoryQueues.Options()));
        return services;
    }

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
        }
        services.AddSingleton(optionsBuilder ?? (static _ => new NatsQueues.Options()));
        return services;
    }
}
