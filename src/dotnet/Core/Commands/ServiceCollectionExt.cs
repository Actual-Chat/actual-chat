using ActualChat.Commands.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Commands;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddLocalCommandQueues(
        this IServiceCollection services,
        Func<IServiceProvider, LocalCommandQueues.Options>? optionsBuilder = null)
    {
        services.AddCommander();
        if (!services.HasService<ICommandQueues>()) {
            services.AddSingleton<ICommandQueues, LocalCommandQueues>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationCompletionListener, EnqueueOnCompletionProcessor>());
        }
        services.AddSingleton(optionsBuilder ?? (static _ => new LocalCommandQueues.Options()));
        return services;
    }

    public static IServiceCollection AddCommandQueueScheduler(
        this IServiceCollection services,
        Action<CommandQueueScheduler.Options>? optionsBuilder = null)
    {
        var options = services.GetSingletonInstance<CommandQueueScheduler.Options>();
        if (options == null) {
            options = new();
            services.AddCommander();
            services.AddSingleton(options);
            services.AddSingleton<CommandQueueScheduler>();
            services.AddHostedService<CommandQueueScheduler>();
        }
        optionsBuilder?.Invoke(options);
        return services;
    }
}
