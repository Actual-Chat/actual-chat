using ActualChat.Commands.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Commands;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddLocalCommandQueues(
        this IServiceCollection services,
        Func<IServiceProvider, LocalCommandQueue.Options>? optionsBuilder = null)
    {
        services.AddCommander();
        if (!services.HasService<ICommandQueues>()) {
            services.AddSingleton<ICommandQueues, LocalCommandQueues>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationCompletionListener, EnqueueOnCompletionProcessor>());
        }
        if (optionsBuilder != null)
            services.AddSingleton(optionsBuilder);
        else
            services.TryAddSingleton(static _ => new LocalCommandQueue.Options());
        return services;
    }

    public static IServiceCollection AddCommandQueueProcessor(
        this IServiceCollection services,
        Symbol queueName,
        Symbol shardKey = default)
        => services.AddCommandQueueProcessor(o => o.AddQueue(queueName, shardKey));

    public static IServiceCollection AddCommandQueueProcessor(
        this IServiceCollection services,
        Action<CommandQueueProcessor.Options>? optionsBuilder = null)
    {
        var options = services.GetSingletonInstance<CommandQueueProcessor.Options>();
        if (options == null) {
            options = new();
            services.AddCommander();
            services.AddSingleton(options);
            services.AddSingleton<CommandQueueProcessor>();
            services.AddHostedService<CommandQueueProcessor>();
        }
        optionsBuilder?.Invoke(options);
        return services;
    }
}
