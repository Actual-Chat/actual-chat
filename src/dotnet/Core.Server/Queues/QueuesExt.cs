namespace ActualChat.Queues;

public static class QueuesExt
{
    // Enqueue

    public static Task Enqueue<TCommand>(this IQueues queues,
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
        => queues.Enqueue(QueuedCommand.New(command), cancellationToken);

    public static Task Enqueue(this IQueues queues,
        QueuedCommand queuedCommand,
        CancellationToken cancellationToken = default)
    {
        var queueRefResolver = queues.Services.GetRequiredService<IQueueRefResolver>();
        var queueShardRef = queueRefResolver.GetQueueShardRef(queuedCommand.UntypedCommand);
        var queueProcessor = queues.GetSender(queueShardRef.QueueRef);
        return queueProcessor.Enqueue(queueShardRef, queuedCommand, cancellationToken);
    }

    // WhenProcessing

    public static Task WhenProcessing(this IQueues queues, CancellationToken cancellationToken = default)
        => queues.WhenProcessing(TimeSpan.FromSeconds(2), cancellationToken);

    public static Task WhenProcessing(this IQueues queues, TimeSpan maxCommandGap, CancellationToken cancellationToken = default)
    {
        var tasks = queues.Processors.Values.Select(x => x.WhenProcessing(maxCommandGap, cancellationToken));
        return Task.WhenAll(tasks);
    }
}
