using ActualChat.Commands.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.OS;

namespace ActualChat.Commands;

public static class FusionBuilderExt
{
    private static readonly ConcurrentDictionary<string, string> _registrations = new (StringComparer.Ordinal);

    public static FusionBuilder AddLocalCommandScheduler(this FusionBuilder fusion, QueueRef queueRef, int degreeOfParallelism = 0)
    {
        if (degreeOfParallelism <= 0)
            degreeOfParallelism = HardwareInfo.ProcessorCount;

        var services = fusion.Services;
        if (!_registrations.TryAdd(queueRef.Name, ""))
            return fusion;

        services.TryAddSingleton<ICommandQueues, LocalCommandQueues>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationCompletionListener, EnqueueOnCompletionProcessor>());
        services.AddHostedService<LocalCommandScheduler>(sp => new LocalCommandScheduler(
            queueRef.Name,
            degreeOfParallelism,
            sp));

        return fusion;
    }
}
