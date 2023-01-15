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
        this IEventCommand eventCommand,
        QueueRef queueRef1,
        QueueRef queueRef2,
        CancellationToken cancellationToken = default)
    {
        var queuedCommand = QueuedCommand.New(eventCommand, queueRef1);
        var commandContext = CommandContext.GetCurrent();
        var queues = commandContext.Services.GetRequiredService<ICommandQueues>();
        var queue1 = queues[queueRef1];
        var queue2 = queues[queueRef2];
        var task1 = queue1.Enqueue(queuedCommand.WithQueueRef(queueRef1), cancellationToken);
        var task2 = queue2.Enqueue(queuedCommand.WithQueueRef(queueRef2), cancellationToken);
        return Task.WhenAll(task1, task2);
    }

    public static Task Enqueue(
        this IEventCommand eventCommand,
        QueueRef queueRef1,
        QueueRef queueRef2,
        QueueRef queueRef3,
        CancellationToken cancellationToken = default)
    {
        var queuedCommand = QueuedCommand.New(eventCommand, queueRef1);
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
        this IEventCommand eventCommand,
        CancellationToken cancellationToken,
        params QueueRef[] queueRefs)
    {
        if (queueRefs.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(queueRefs));

        var queuedCommand = QueuedCommand.New(eventCommand);
        var commandContext = CommandContext.GetCurrent();
        var queues = commandContext.Services.GetRequiredService<ICommandQueues>();

        var tasks = queueRefs
            .Select(queueRef => queues[queueRef].Enqueue(queuedCommand.WithQueueRef(queueRef), cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public static TCommand EnqueueOnCompletion<TCommand>(this TCommand command, QueueRef queueRef)
        where TCommand : ICommand
    {
        var queuedCommand = QueuedCommand.New(command, queueRef);
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation is already completed.");

        var operationItems = GetOperation(commandContext).Items;
        var list = operationItems.GetOrDefault(ImmutableList<QueuedCommand>.Empty);
        list = list.Add(queuedCommand);
        operationItems.Set(list);
        return command;
    }

    public static TCommand EnqueueOnCompletion<TCommand>(
        this TCommand command,
        QueueRef queueRef1,
        QueueRef queueRef2)
        where TCommand : IEventCommand
    {
        var queuedCommand = QueuedCommand.New(command, queueRef1);
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation is already completed.");

        var operationItems = GetOperation(commandContext).Items;
        var list = operationItems.GetOrDefault(ImmutableList<QueuedCommand>.Empty)
            .Add(queuedCommand.WithQueueRef(queueRef1))
            .Add(queuedCommand.WithQueueRef(queueRef2));
        operationItems.Set(list);
        return command;
    }

    public static TCommand EnqueueOnCompletion<TCommand>(
        this TCommand command,
        QueueRef queueRef1,
        QueueRef queueRef2,
        QueueRef queueRef3)
        where TCommand : IEventCommand
    {
        var queuedCommand = QueuedCommand.New(command, queueRef1);
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation is already completed.");

        var operationItems = GetOperation(commandContext).Items;
        var list = operationItems.GetOrDefault(ImmutableList<QueuedCommand>.Empty)
            .Add(queuedCommand.WithQueueRef(queueRef1))
            .Add(queuedCommand.WithQueueRef(queueRef2))
            .Add(queuedCommand.WithQueueRef(queueRef3));
        operationItems.Set(list);
        return command;
    }

    // Shortcuts

    public static TCommand EnqueueOnCompletion<TCommand>(this TCommand command)
        where TCommand : ICommand
        => command.EnqueueOnCompletion(Queues.Default);
    public static TCommand EnqueueOnCompletion<TCommand>(this TCommand command, UserId userId)
        where TCommand : ICommand
        => command.EnqueueOnCompletion(Queues.Users.ShardBy(userId));
    public static TCommand EnqueueOnCompletion<TCommand>(this TCommand command, ChatId chatId)
        where TCommand : ICommand
        => command.EnqueueOnCompletion(Queues.Chats.ShardBy(chatId));
    public static TCommand EnqueueOnCompletion<TCommand>(this TCommand command, UserId userId, ChatId chatId)
        where TCommand : IEventCommand
        => command.EnqueueOnCompletion(Queues.Users.ShardBy(userId), Queues.Chats.ShardBy(chatId));

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
