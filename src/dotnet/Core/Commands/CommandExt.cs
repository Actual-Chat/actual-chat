namespace ActualChat.Commands;

public static class CommandExt
{
    public static Task Enqueue<TCommand>(
        this TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand, IHasShardKey
        => Enqueue(CommandContext.GetCurrent().Services.GetRequiredService<ICommandQueues>(),
            command,
            cancellationToken);

    public static Task Enqueue<TCommand>(
        this ICommandQueues queues,
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand: ICommand, IHasShardKey
    {
        var queuedCommand = QueuedCommand.New(command);
        var queueIdProvider = queues.Services.GetRequiredService<ICommandQueueIdProvider>();
        var queueId = queueIdProvider.Get(queuedCommand);
        var queue = queues[queueId];
        return queue.Enqueue(queuedCommand, cancellationToken);
    }

    public static TCommand EnqueueOnCompletion<TCommand>(this TCommand command)
        where TCommand : ICommand, IHasShardKey
    {
        var queuedCommand = QueuedCommand.New(command);
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
