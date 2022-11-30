namespace ActualChat.Commands;

public interface IQueuedCommand
{
    string Id { get; }
    ICommand Command { get; }
}
