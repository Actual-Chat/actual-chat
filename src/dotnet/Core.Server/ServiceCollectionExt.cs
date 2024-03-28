using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Queues;
using ActualChat.Queues.InMemory;
using ActualChat.Queues.Internal;
using ActualChat.Queues.Nats;
using ActualChat.Rpc;
using ActualLab.CommandR.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.Core;
using NATS.Client.Hosting;

namespace ActualChat;

public static class ServiceCollectionExt
{
    // HasService

    public static bool HasService<TService>(this IServiceCollection services, object serviceKey)
        => services.HasService(typeof(TService), serviceKey);
    public static bool HasService(this IServiceCollection services, Type serviceType, object serviceKey)
        => services.Any(d => d.ServiceType == serviceType && ReferenceEquals(d.ServiceKey, serviceKey));

    // AddRpcHost

    public static RpcHostBuilder AddRpcHost(
        this IServiceCollection services, HostInfo hostInfo, ILogger? log = null)
        => new(services, hostInfo, log);

    public static IServiceCollection AddNats(this IServiceCollection services, HostInfo hostInfo, NatsSettings settings)
    {
        if (!services.HasService<NatsConnection>())
            services.AddNats(poolSize: 1, options => {
                var natsTimeout = TimeSpan.FromSeconds(hostInfo.IsDevelopmentInstance ? 300 : 10);
                return options with {
                    Url = settings.Url,
                    TlsOpts = new NatsTlsOpts { Mode = TlsMode.Auto, },
                    AuthOpts = settings.Seed.IsNullOrEmpty() || settings.NKey.IsNullOrEmpty()
                        ? NatsAuthOpts.Default
                        : new NatsAuthOpts { Seed = settings.Seed, NKey = settings.NKey, },
                    CommandTimeout = natsTimeout,
                    ConnectTimeout = natsTimeout,
                    RequestTimeout = natsTimeout,
                };
            });
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
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationCompletionListener, EnqueueOnCompletionProcessor>());
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
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationCompletionListener, EnqueueOnCompletionProcessor>());
        }
        services.AddSingleton(optionsBuilder ?? (static _ => new NatsQueues.Options()));
        return services;
    }
}
