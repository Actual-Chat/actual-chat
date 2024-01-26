using System.Diagnostics.CodeAnalysis;
using ActualChat.Commands.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ActualLab.CommandR.Internal;

namespace ActualChat.Commands;

public static class ServiceCollectionExt
{
    [RequiresUnreferencedCode(UnreferencedCode.Commander)]
    public static IServiceCollection AddLocalCommandQueues(
        this IServiceCollection services,
        Func<IServiceProvider, LocalCommandQueues.Options>? optionsBuilder = null)
    {
        services.AddCommander();
        if (!services.HasService<ICommandQueues>()) {
            services.AddSingleton<LocalCommandQueues>(c => new LocalCommandQueues(c.GetRequiredService<LocalCommandQueues.Options>(), c));
            services.AddSingleton<ICommandQueues>(c => c.GetRequiredService<LocalCommandQueues>());
            services.AddSingleton<ICommandQueueIdProvider>(c => new LocalCommandQueueIdProvider());
            services.AddSingleton<LocalCommandQueueScheduler>();
            services.AddHostedService<LocalCommandQueueScheduler>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationCompletionListener, EnqueueOnCompletionProcessor>());
        }
        services.AddSingleton(optionsBuilder ?? (static _ => new LocalCommandQueues.Options()));
        return services;
    }
}
