
namespace ActualChat.Queues;

public static class CommandExt
{
    private static readonly Action<IEventCommand, string> ChainIdSetter =
        typeof(IEventCommand).GetProperty(nameof(IEventCommand.ChainId))!.GetSetter<string>();

    public static CommandKind GetKind(this ICommand command)
        => command is IEventCommand eventCommand
            ? eventCommand.ChainId.IsNullOrEmpty()
                ? CommandKind.UnboundEvent
                : CommandKind.BoundEvent
            : CommandKind.Command;

    // Internal methods

    internal static TCommand WithChainId<TCommand>(this TCommand command, Symbol chainId)
        where TCommand: IEventCommand
    {
        var clone = MemberwiseCloner.Invoke(command);
        ChainIdSetter.Invoke(clone, chainId);
        return clone;
    }
}
