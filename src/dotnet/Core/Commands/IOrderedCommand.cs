namespace ActualChat;

/// <summary>
/// What the client command queue does with the commands already waiting in a partition
/// when a new one arrives.
/// </summary>
public enum CommandCoalescing
{
    // Every command runs; the only safe choice for commands that aren't idempotent
    None = 0,
    ReplaceWaiting,
}

/// <summary>
/// A command the client runs one at a time per <see cref="PartitionKey"/>, in the order they were sent.
/// An empty key means no ordering.
/// </summary>
public interface IOrderedCommand : ICommand
{
    string PartitionKey { get; }
    CommandCoalescing Coalescing => CommandCoalescing.None;
}
