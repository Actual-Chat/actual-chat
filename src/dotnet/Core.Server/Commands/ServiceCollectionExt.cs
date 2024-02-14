using System.Diagnostics.CodeAnalysis;
using ActualChat.Commands.Internal;
using ActualChat.Hosting;
using ActualLab.CommandR.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Commands;

public static class ServiceCollectionExt
{
    [RequiresUnreferencedCode(UnreferencedCode.Commander)]
    public static IServiceCollection AddCommandQueues(
        this IServiceCollection services,
        HostRole hostRole,
        Func<IServiceProvider, NatsCommandQueues.Options>? optionsBuilder = null,
        Func<IServiceProvider, ShardCommandQueueScheduler.Options>? schedulerOptionsBuilder = null)
    {
        var serviceKey = hostRole.Id.Value;
        services.AddCommander();
        if (!services.HasService<ICommandQueues>()) {
            services.AddSingleton<ICommandQueues, NatsCommandQueues>();
            services.AddSingleton<ICommandQueueIdProvider>(c => new ShardCommandQueueIdProvider(c));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationCompletionListener, EnqueueOnCompletionProcessor>());
        }
        if (!services.HasService<NatsCommandQueues.Options>())
            services.AddSingleton(optionsBuilder ?? (static _ => new NatsCommandQueues.Options()));
        if (!services.HasService<ShardCommandQueueScheduler>(serviceKey) && optionsBuilder != null)
            services.AddKeyedSingleton(serviceKey, (s, _) => optionsBuilder(s));

        if (!services.HasService<ShardCommandQueueScheduler>(serviceKey)) {
            services.AddKeyedSingleton<ShardCommandQueueScheduler>(serviceKey, (c, _) => new ShardCommandQueueScheduler(hostRole, c));
            services.AddKeyedSingleton<IHostedService, ShardCommandQueueScheduler>(serviceKey, (c, key) => c.GetRequiredKeyedService<ShardCommandQueueScheduler>(key));
        }
        if (!services.HasService<ShardCommandQueueScheduler.Options>())
            services.AddSingleton(schedulerOptionsBuilder ?? (static _ => new ShardCommandQueueScheduler.Options()));
        if (!services.HasService<ShardCommandQueueScheduler.Options>(serviceKey) && schedulerOptionsBuilder != null)
            services.AddKeyedSingleton(serviceKey, (s, _) => schedulerOptionsBuilder(s));

        return services;
    }
}
