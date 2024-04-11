namespace ActualChat.Queues;

public static class CommandExt
{
    private static readonly Action<IEventCommand, Symbol> ChainIdSetter =
        typeof(IEventCommand).GetProperty(nameof(IEventCommand.ChainId))!.GetSetter<Symbol>();

    public static CommandKind GetKind(this ICommand command)
        => command is IEventCommand eventCommand
            ? eventCommand.ChainId.IsEmpty
                ? CommandKind.UnboundEvent
                : CommandKind.BoundEvent
            : CommandKind.Command;

    public static Task Enqueue<TCommand>(
        this TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        var commandContext = CommandContext.GetCurrent();
        var queues = commandContext.Services.Queues();
        return queues.Enqueue(command, cancellationToken);
    }

    public static TCommand EnqueueOnCompletion<TCommand>(this TCommand command)
        where TCommand : ICommand
    {
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation has already completed.");

        var context = CommandContext.GetCurrent();
        var operationItems = context.Operation.Items;
        var list = operationItems.GetOrDefault(ImmutableList<QueuedCommand>.Empty);
        list = list.Add(QueuedCommand.New(command));
        operationItems.Set(list);
        return command;
    }

    // Internal methods

    public static TCommand WithChainId<TCommand>(this TCommand command, Symbol chainId)
        where TCommand: IEventCommand
    {
        var clone = MemberwiseCloner.Invoke(command);
        ChainIdSetter.Invoke(clone, chainId);
        return clone;
    }
}
