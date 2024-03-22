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
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation is already completed.");

        var operationItems = GetOperation(commandContext).Items;
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
