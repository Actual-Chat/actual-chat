namespace ActualChat.Commands.Internal;

public record struct EnqueuedCommandEntry(ICommand Command, QueueRef QueueRef);
