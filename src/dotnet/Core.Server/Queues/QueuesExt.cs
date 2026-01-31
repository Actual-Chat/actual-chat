using ActualChat.Diagnostics;
using ActualChat.Queues.Internal;
using ActualLab.Resilience;
using Microsoft.Extensions.Primitives;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace ActualChat.Queues;

public static class QueuesExt
{
    public static readonly TimeSpan QueueTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SlowQueueTimeout = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultTimeout = QueueTimeout;

    // Enqueue

    public static Task Enqueue<TCommand>(this IQueues queues,
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
        => queues.Enqueue(command, null, cancellationToken);

    public static async Task Enqueue<TCommand>(this IQueues queues,
        TCommand command,
        string? uuid = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        var operationName = command.GetOperationName("enqueue");
        using var activity = CoreServerInstruments.ActivitySource.StartActivity(operationName, ActivityKind.Producer);
        Dictionary<string, StringValues>? contextHeaders = null;
        if (activity is { Context: var activityContext }) {
            var propagationContext = new PropagationContext(activityContext, Baggage.Current);
            contextHeaders = new Dictionary<string, StringValues>(StringComparer.Ordinal);
            Propagators.DefaultTextMapPropagator.Inject(
                propagationContext, contextHeaders, static (headers, key, value) => headers[key] = value);
            activity.AddTag(OtelConstants.MessagingOperation, "enqueue");
            activity.AddTag(OtelConstants.MessagingMessageType, command.GetType().Name);
        }
        try {
            var queuedCommand = QueuedCommand.New(command, uuid, headers: contextHeaders, queues: queues);
            await queues.Enqueue(queuedCommand, cancellationToken).ConfigureAwait(false);
            activity?.SetTag(OtelConstants.MessagingMessageId, queuedCommand.Uuid);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception e) {
            activity?.AddException(e);
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }

    public static Task Enqueue(this IQueues queues,
        QueuedCommand queuedCommand,
        CancellationToken cancellationToken = default)
    {
        var queueShardRef = QueueShardRef.For(queuedCommand.UntypedCommand, queues.Services);
        var sender = queues.GetSender(queueShardRef.QueueRef);
        return sender.Enqueue(queueShardRef, queuedCommand, cancellationToken);
    }

    // WhenProcessing

    public static Task WhenProcessing(this IQueues queues, CancellationToken cancellationToken = default)
        => queues.WhenProcessing(TimeSpan.FromSeconds(3), cancellationToken);

    public static Task WhenProcessing(this IQueues queues, TimeSpan maxCommandGap, CancellationToken cancellationToken = default)
    {
        var tasks = queues.Processors.Values.Select(x => x.WhenProcessing(maxCommandGap, cancellationToken));
        return Task.WhenAll(tasks);
    }

    // Internal methods

    internal static TimeSpan GetTimeout(ICommand command, IServiceProvider services)
    {
        if (command is ITimeoutProvider timeoutProvider)
            return timeoutProvider.GetTimeout(services);

        return (command as IHasTimeout)?.Timeout ?? DefaultTimeout;
    }
}
