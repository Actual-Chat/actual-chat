namespace ActualChat.Commands;

public interface IEventHandlerResolver
{
    IReadOnlyList<IReadOnlyList<CommandHandler>> GetEventHandlers(Type commandType);
}
