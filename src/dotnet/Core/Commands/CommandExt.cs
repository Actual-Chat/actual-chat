namespace ActualChat.Commands;

public static class CommandExt
{
    public static Task Enqueue(
        this ICommand command,
        QueueRef queueRef,
        CancellationToken cancellationToken = default)
    {
        var queuedCommand = QueuedCommand.New(command, queueRef);
        var commandContext = CommandContext.GetCurrent();
        var queues = commandContext.Services.GetRequiredService<ICommandQueues>();
        var queue = queues[queueRef];
        return queue.Enqueue(queuedCommand, cancellationToken);
    }

    public static Task Enqueue(
        this IEventCommand @event,
        QueueRef queueRef1,
        QueueRef queueRef2,
        CancellationToken cancellationToken = default)
    {
        var queuedCommand = QueuedCommand.New(@event, queueRef1);
        var commandContext = CommandContext.GetCurrent();
        var queues = commandContext.Services.GetRequiredService<ICommandQueues>();
        var queue1 = queues[queueRef1];
        var queue2 = queues[queueRef2];
        var task1 = queue1.Enqueue(queuedCommand.WithQueueRef(queueRef1), cancellationToken);
        var task2 = queue2.Enqueue(queuedCommand.WithQueueRef(queueRef2), cancellationToken);
        return Task.WhenAll(task1, task2);
    }

    public static Task Enqueue(
        this IEventCommand @event,
        QueueRef queueRef1,
        QueueRef queueRef2,
        QueueRef queueRef3,
        CancellationToken cancellationToken = default)
    {
        var queuedCommand = QueuedCommand.New(@event, queueRef1);
        var commandContext = CommandContext.GetCurrent();
        var queues = commandContext.Services.GetRequiredService<ICommandQueues>();
        var queue1 = queues[queueRef1];
        var queue2 = queues[queueRef2];
        var queue3 = queues[queueRef3];
        var task1 = queue1.Enqueue(queuedCommand.WithQueueRef(queueRef1), cancellationToken);
        var task2 = queue2.Enqueue(queuedCommand.WithQueueRef(queueRef2), cancellationToken);
        var task3 = queue3.Enqueue(queuedCommand.WithQueueRef(queueRef3), cancellationToken);
        return Task.WhenAll(task1, task2, task3);
    }

    public static async Task Enqueue(
        this IEventCommand @event,
        CancellationToken cancellationToken,
        params QueueRef[] queueRefs)
    {
        if (queueRefs.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(queueRefs));

        var queuedCommand = QueuedCommand.New(@event);
        var commandContext = CommandContext.GetCurrent();
        var queues = commandContext.Services.GetRequiredService<ICommandQueues>();

        var tasks = queueRefs
            .Select(queueRef => queues[queueRef].Enqueue(queuedCommand.WithQueueRef(queueRef), cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public static void EnqueueOnCompletion(this ICommand command, QueueRef queueRef)
    {
        var queuedCommand = QueuedCommand.New(command, queueRef);
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation is already completed.");

        var operationItems = GetOperation(commandContext).Items;
        var list = operationItems.GetOrDefault(ImmutableList<QueuedCommand>.Empty);
        list = list.Add(queuedCommand);
        operationItems.Set(list);
    }

    public static void EnqueueOnCompletion(
        this IEventCommand @event,
        QueueRef queueRef1,
        QueueRef queueRef2)
    {
        var queuedCommand = QueuedCommand.New(@event, queueRef1);
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation is already completed.");

        var operationItems = GetOperation(commandContext).Items;
        var list = operationItems.GetOrDefault(ImmutableList<QueuedCommand>.Empty)
            .Add(queuedCommand.WithQueueRef(queueRef1))
            .Add(queuedCommand.WithQueueRef(queueRef2));
        operationItems.Set(list);
    }

    public static void EnqueueOnCompletion(
        this IEventCommand @event,
        QueueRef queueRef1,
        QueueRef queueRef2,
        QueueRef queueRef3)
    {
        var queuedCommand = QueuedCommand.New(@event, queueRef1);
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation is already completed.");

        var operationItems = GetOperation(commandContext).Items;
        var list = operationItems.GetOrDefault(ImmutableList<QueuedCommand>.Empty)
            .Add(queuedCommand.WithQueueRef(queueRef1))
            .Add(queuedCommand.WithQueueRef(queueRef2))
            .Add(queuedCommand.WithQueueRef(queueRef3));
        operationItems.Set(list);
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
