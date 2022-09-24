namespace ActualChat.Commands;

public record QueuedCommand(IBackendCommand Command, ImmutableArray<QueueRef> QueueRefs);
