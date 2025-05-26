using ActualLab.CommandR.Operations;

namespace ActualChat.Queues;

public static class CommandExt
{
    private static readonly Action<IEventCommand, string> ChainIdSetter =
        typeof(IEventCommand).GetProperty(nameof(IEventCommand.ChainId))!.GetSetter<string>();

    public static bool HasDelay(this ICommand command, Moment now, [NotNullWhen(true)] out TimeSpan? delay)
    {
        if (command is IHasDelayUntil delayed && delayed.DelayUntil > now) {
            delay = delayed.DelayUntil - now;
            return true;
        }

        delay = null;
        return false;
    }

    public static CommandKind GetKind(this ICommand command)
        => command is IEventCommand eventCommand
            ? eventCommand.ChainId.IsNullOrEmpty()
                ? CommandKind.UnboundEvent
                : CommandKind.BoundEvent
            : CommandKind.Command;

    public static Task EnqueueDirectly<TCommand>(
        this TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        var commandContext = CommandContext.GetCurrent();
        var queues = commandContext.Services.Queues();
        return queues.Enqueue(command, cancellationToken);
    }

    // Internal methods

    internal static TCommand WithChainId<TCommand>(this TCommand command, Symbol chainId)
        where TCommand: IEventCommand
    {
        var clone = MemberwiseCloner.Invoke(command);
        ChainIdSetter.Invoke(clone, chainId);
        return clone;
    }
}
