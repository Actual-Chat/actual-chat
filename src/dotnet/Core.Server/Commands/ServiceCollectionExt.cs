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
        IReadOnlyCollection<HostRole> hostRoles,
        Func<IServiceProvider, NatsCommandQueues.Options>? optionsBuilder = null,
        Func<IServiceProvider, ShardCommandQueueScheduler.Options>? schedulerOptionsBuilder = null,
        Func<IServiceProvider, ShardEventQueueScheduler.Options>? eventSchedulerOptionsBuilder = null)
    {
        var backendRoles = hostRoles.Where(r => r.IsBackend);
        foreach (var hostRole in backendRoles) {
            services.AddCommandQueues(hostRole, optionsBuilder, schedulerOptionsBuilder);

            // register EventScheduler
            var serviceKey = hostRole.Id.Value;
            if (!services.HasService<ShardEventQueueScheduler>(serviceKey)) {
                services.AddKeyedSingleton<ShardEventQueueScheduler>(serviceKey, (c, _) => new ShardEventQueueScheduler(hostRole, c));
                services.AddSingleton<IHostedService, ShardEventQueueScheduler>(c => c.GetRequiredKeyedService<ShardEventQueueScheduler>(serviceKey));
            }
            if (!services.HasService<ShardEventQueueScheduler.Options>())
                services.AddSingleton(eventSchedulerOptionsBuilder ?? (static _ => new ShardEventQueueScheduler.Options()));
            if (!services.HasService<ShardEventQueueScheduler.Options>(serviceKey) && eventSchedulerOptionsBuilder != null)
                services.AddKeyedSingleton(serviceKey, (s, _) => eventSchedulerOptionsBuilder(s));
        }
        return services;
    }

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
        if (!services.HasService<NatsCommandQueues.Options>(serviceKey) && optionsBuilder != null)
            services.AddKeyedSingleton(serviceKey, (s, _) => optionsBuilder(s));

        if (!services.HasService<ShardCommandQueueScheduler>(serviceKey)) {
            services.AddKeyedSingleton<ShardCommandQueueScheduler>(serviceKey, (c, _) => new ShardCommandQueueScheduler(hostRole, c));
            services.AddSingleton<IHostedService, ShardCommandQueueScheduler>(c => c.GetRequiredKeyedService<ShardCommandQueueScheduler>(serviceKey));
            services.AddKeyedSingleton<ICommandQueueScheduler>(serviceKey, (c, _) => c.GetRequiredKeyedService<ShardCommandQueueScheduler>(serviceKey));
        }
        if (!services.HasService<ShardCommandQueueScheduler.Options>())
            services.AddSingleton(schedulerOptionsBuilder ?? (static _ => new ShardCommandQueueScheduler.Options()));
        if (!services.HasService<ShardCommandQueueScheduler.Options>(serviceKey) && schedulerOptionsBuilder != null)
            services.AddKeyedSingleton(serviceKey, (s, _) => schedulerOptionsBuilder(s));

        return services;
    }
}
