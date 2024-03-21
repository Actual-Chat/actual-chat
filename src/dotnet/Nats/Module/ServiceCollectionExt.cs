using System.Diagnostics.CodeAnalysis;
using ActualChat.Commands;
using ActualChat.Commands.Internal;
using ActualChat.Hosting;
using ActualLab.CommandR.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Nats.Module;

public static class ServiceCollectionExt
{
    [RequiresUnreferencedCode(UnreferencedCode.Commander)]
    public static IServiceCollection AddCommandQueues(
        this IServiceCollection services,
        IReadOnlySet<HostRole> hostRoles,
        Func<IServiceProvider, NatsCommandQueues.Options>? optionsBuilder = null)
    {
        // register command queue workers for backend roles with ShardScheme defined
        var backendRoles = hostRoles
            .Where(r => r.IsBackend && ShardScheme.ById.ContainsKey(r.Id))
            .ToHashSet();

        foreach (var hostRole in backendRoles)
            services.AddCommandQueues(hostRole, optionsBuilder);

        return services;
    }

    [RequiresUnreferencedCode(UnreferencedCode.Commander)]
    public static IServiceCollection AddCommandQueues(
        this IServiceCollection services,
        HostRole hostRole,
        Func<IServiceProvider, NatsCommandQueues.Options>? optionsBuilder = null,
        Func<IServiceProvider, ShardCommandQueueScheduler.Options>? schedulerOptionsBuilder = null,
        Func<IServiceProvider, ShardEventQueueScheduler.Options>? eventSchedulerOptionsBuilder = null)
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

        // register CommandScheduler

        if (!services.HasService<ShardCommandQueueScheduler>(serviceKey)) {
            services.AddKeyedSingleton<ShardCommandQueueScheduler>(serviceKey, (c, _) => new ShardCommandQueueScheduler(hostRole, c));
            services.AddSingleton<IHostedService, ShardCommandQueueScheduler>(c => c.GetRequiredKeyedService<ShardCommandQueueScheduler>(serviceKey));
            services.AddKeyedSingleton<ICommandQueueScheduler>(serviceKey, (c, _) => c.GetRequiredKeyedService<ShardCommandQueueScheduler>(serviceKey));
        }
        if (!services.HasService<ShardCommandQueueScheduler.Options>())
            services.AddSingleton(schedulerOptionsBuilder ?? (static _ => new ShardCommandQueueScheduler.Options()));
        if (!services.HasService<ShardCommandQueueScheduler.Options>(serviceKey) && schedulerOptionsBuilder != null)
            services.AddKeyedSingleton(serviceKey, (s, _) => schedulerOptionsBuilder(s));

        // register EventScheduler

        if (hostRole == HostRole.OneBackendServer) {
            // keep single EventScheduler for the host and remove all other registration
            services.RemoveAll(sd =>
                sd.ServiceType == typeof(IHostedService)
                && sd.ImplementationFactory != null
                && sd.ImplementationFactory.Method == HostEventQueueSchedulerFactory.GetEventSchedulerMethod);
            services.RemoveAll(sd =>
                sd.ServiceType == typeof(ICommandQueueScheduler)
                && ReferenceEquals(sd.ServiceKey, serviceKey));
            services.RemoveAll(sd => sd.ServiceType == typeof(ShardEventQueueScheduler));
        }
        else if (services.HasService<ShardEventQueueScheduler>(HostRole.OneBackendServer.Id.Value))
            return services; // single event handler for all backend roles has already been registered

        if (hostRole.IsQueue)
            return services; // queue roles do not implement event handlers

        if (!services.HasService<ShardEventQueueScheduler>(serviceKey)) {
            var schedulerFactory = new HostEventQueueSchedulerFactory(serviceKey);
            services.AddKeyedSingleton<ShardEventQueueScheduler>(serviceKey, (c, _) => new ShardEventQueueScheduler(hostRole, c));
            services.AddSingleton<IHostedService, ShardEventQueueScheduler>(schedulerFactory.GetEventScheduler);
            services.AddKeyedSingleton<ICommandQueueScheduler, ShardEventQueueScheduler>(serviceKey, schedulerFactory.GetKeyedEventScheduler);
        }
        if (!services.HasService<ShardEventQueueScheduler.Options>())
            services.AddSingleton(eventSchedulerOptionsBuilder ?? (static _ => new ShardEventQueueScheduler.Options()));
        if (!services.HasService<ShardEventQueueScheduler.Options>(serviceKey) && eventSchedulerOptionsBuilder != null)
            services.AddKeyedSingleton(serviceKey, (s, _) => eventSchedulerOptionsBuilder(s));

        return services;
    }

    private class HostEventQueueSchedulerFactory(string serviceKey)
    {
        public static MethodInfo GetEventSchedulerMethod { get; }
            = typeof(HostEventQueueSchedulerFactory).GetMethod(nameof(GetEventScheduler))!;
        public static MethodInfo GetKeyedEventSchedulerMethod { get; }
            = typeof(HostEventQueueSchedulerFactory).GetMethod(nameof(GetKeyedEventScheduler))!;

        public ShardEventQueueScheduler GetEventScheduler(IServiceProvider services)
            => services.GetRequiredKeyedService<ShardEventQueueScheduler>(serviceKey);

        public ShardEventQueueScheduler GetKeyedEventScheduler(IServiceProvider services, object key)
            => services.GetRequiredKeyedService<ShardEventQueueScheduler>(serviceKey);
    }
}
