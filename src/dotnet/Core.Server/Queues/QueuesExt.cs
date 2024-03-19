namespace ActualChat.Queues;

public static class QueuesExt
{
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
        var queueProcessor = queues.GetProcessor(queueShardRef.QueueRef);
        return queueProcessor.Enqueue(queueShardRef, queuedCommand, cancellationToken);
    }
}
