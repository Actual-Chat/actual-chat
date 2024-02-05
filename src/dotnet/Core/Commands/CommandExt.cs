namespace ActualChat.Commands;

public static class CommandExt
{
    public static Task Enqueue<TCommand>(
        this TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand: ICommand, IHasShardKey
        => Enqueue(command, default, cancellationToken);

    public static Task Enqueue<TCommand>(
        this TCommand command,
        QueuedCommandPriority priority,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand, IHasShardKey
        => Enqueue(CommandContext.GetCurrent().Services.GetRequiredService<ICommandQueues>(),
            command,
            priority,
            cancellationToken);

    public static Task Enqueue<TCommand>(
        this ICommandQueues queues,
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand, IHasShardKey
        => Enqueue(queues, command, QueuedCommandPriority.Normal, cancellationToken);

    public static Task Enqueue<TCommand>(
        this ICommandQueues queues,
        TCommand command,
        QueuedCommandPriority priority,
        CancellationToken cancellationToken = default)
        where TCommand: ICommand, IHasShardKey
    {
        var queuedCommand = QueuedCommand.New(command, priority);
        var queueIdProvider = queues.Services.GetRequiredService<ICommandQueueIdProvider>();
        var queueId = queueIdProvider.Get(queuedCommand);
        var queue = queues[queueId];
        return queue.Enqueue(queuedCommand, cancellationToken);
    }

    public static TCommand EnqueueOnCompletion<TCommand>(
        this TCommand command,
        QueuedCommandPriority priority = default)
        where TCommand : ICommand, IHasShardKey
    {
        var queuedCommand = QueuedCommand.New(command, priority);
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation is already completed.");

        var operationItems = GetOperation(commandContext).Items;
        var list = operationItems.GetOrDefault(ImmutableList<QueuedCommand>.Empty);
        list = list.Add(queuedCommand);
        operationItems.Set(list);
        return command;
    }

    public static QueuedCommand WithRetry(this QueuedCommand command)
    {
        var id = command.Id.Value;
        if (id.OrdinalIndexOf(" @retry-") is var retrySuffixStart and >= 0)
            id = id[..retrySuffixStart];
        var newTryIndex = command.TryIndex + 1;
        var newCommand = command with {
            Id = $"{id} @retry-{newTryIndex.Format()}",
            TryIndex = newTryIndex,
        };
        return newCommand;
    }

    // Private methods

    private static IOperation GetOperation(CommandContext? commandContext)
    {
        while (commandContext != null) {
            if (commandContext.Items.TryGet<IOperation>(out var operation))
                return operation;
            commandContext = commandContext.OuterContext;
        }
        throw StandardError.Internal("No operation is running.");
    }
}
