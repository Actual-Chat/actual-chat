using ActualChat.Commands.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Commands;

public static class FusionBuilderExt
{
    public static FusionBuilder AddLocalCommandScheduler(this FusionBuilder fusion)
    {
        var services = fusion.Services;
        if (services.HasService<LocalCommandQueue>())
            return fusion;

        services.TryAddSingleton<LocalCommandQueue>();
        services.TryAddSingleton<ICommandQueue>(c => c.GetRequiredService<LocalCommandQueue>());
        services.TryAddSingleton<ICommandQueues, LocalCommandQueues>();
        services.AddHostedService<LocalCommandScheduler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationCompletionListener, EnqueueOnCompletionProcessor>());
        return fusion;
    }
}
