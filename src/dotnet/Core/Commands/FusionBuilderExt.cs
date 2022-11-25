using ActualChat.Commands.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
        if (!_registrations.TryAdd(queueRef.Name, "")) {
            if (services.Any(sd => sd.ServiceType == typeof (ICommandQueues)))
                return fusion;

            _registrations.TryRemove(queueRef.Name, out _);
        }

        services.TryAddSingleton<ICommandQueues, LocalCommandQueues>();
        services.TryAddSingleton<EventCommander, EventCommander>();
        services.TryAddSingleton<IEventHandlerResolver, EventHandlerResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationCompletionListener, EnqueueOnCompletionProcessor>());
        var schedulerServiceDescriptor = ServiceDescriptor.Singleton<IHostedService>(sp => new LocalCommandScheduler(
            queueRef.Name,
            degreeOfParallelism,
            sp,
            name => _registrations.TryRemove(name, out _)));
        services.Add(schedulerServiceDescriptor);

        return fusion;
    }
}
