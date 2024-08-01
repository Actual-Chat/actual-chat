using ActualChat.Module;
using Microsoft.Extensions.Primitives;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace ActualChat.Queues;

public static class QueuesExt
{
    private static ActivitySource ActivitySource {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => CoreServerModuleInstrumentation.ActivitySource;
    }

    private static class ActivityName<TCommand> {
        public static readonly string Enqueue = $"{nameof(Enqueue)}<{typeof(TCommand).Name}>@{nameof(QueuesExt)}";
    }

    // Enqueue

    public static Task Enqueue<TCommand>(this IQueues queues,
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        using var activity = ActivitySource.StartActivity(ActivityName<TCommand>.Enqueue, ActivityKind.Producer);
        Dictionary<string, StringValues>? contextHeaders = null;
        if (activity is Activity { Context: var activityContext }) {
            var propagationContext = new PropagationContext(activityContext, Baggage.Current);
            contextHeaders = new Dictionary<string, StringValues>(StringComparer.Ordinal);
            Propagators.DefaultTextMapPropagator.Inject(
                propagationContext, contextHeaders, static (headers, key, value) => headers[key] = value);
        }
        return queues.Enqueue(QueuedCommand.New(command, headers: contextHeaders), cancellationToken);
    }

    public static Task Enqueue(this IQueues queues,
        QueuedCommand queuedCommand,
        CancellationToken cancellationToken = default)
    {
        var queueRefResolver = queues.Services.GetRequiredService<IQueueRefResolver>();
        var command = queuedCommand.UntypedCommand;
        var requester = new Requester(command,
            static c => $"{nameof(QueuesExt)}.{nameof(Enqueue)}({c?.GetType().GetName() ?? "null"})");
        var queueShardRef = queueRefResolver.GetQueueShardRef(command, requester);
        var queueProcessor = queues.GetSender(queueShardRef.QueueRef);
        return queueProcessor.Enqueue(queueShardRef, queuedCommand, cancellationToken);
    }

    // WhenProcessing

    public static Task WhenProcessing(this IQueues queues, CancellationToken cancellationToken = default)
        => queues.WhenProcessing(TimeSpan.FromSeconds(3), cancellationToken);

    public static Task WhenProcessing(this IQueues queues, TimeSpan maxCommandGap, CancellationToken cancellationToken = default)
    {
        var tasks = queues.Processors.Values.Select(x => x.WhenProcessing(maxCommandGap, cancellationToken));
        return Task.WhenAll(tasks);
    }
}
