namespace ActualChat;

/// <summary>
/// What the client queue does with the commands already waiting in a partition
/// when a new one arrives.
/// </summary>
public enum QueuedCommandCoalescing
{
    // Every command runs; the only safe choice for commands that aren't idempotent
    None = 0,
    ReplaceWaiting,
}

public interface IQueuedCommand : ICommand
{
    string PartitionKey { get; }
    QueuedCommandCoalescing Coalescing => QueuedCommandCoalescing.None;
}
