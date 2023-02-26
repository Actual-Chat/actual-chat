namespace ActualChat.Commands;

public static class CommandExt
{
    public static Task Enqueue(
        this ICommand command,
        CancellationToken cancellationToken = default)
        => Enqueue(command, default, cancellationToken);

    public static Task Enqueue(
        this ICommand command,
        QueuedCommandPriority priority,
        CancellationToken cancellationToken = default)
    {
        var queuedCommand = QueuedCommand.New(command, priority);
        var commandContext = CommandContext.GetCurrent();
        var queues = commandContext.Services.GetRequiredService<ICommandQueues>();
        var queue = queues[queuedCommand.QueueId];
        return queue.Enqueue(queuedCommand, cancellationToken);
    }

    public static TCommand EnqueueOnCompletion<TCommand>(
        this TCommand command,
        QueuedCommandPriority priority = default)
        where TCommand : ICommand
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
