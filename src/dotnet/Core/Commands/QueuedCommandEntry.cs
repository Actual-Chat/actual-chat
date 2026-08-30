namespace ActualChat;

/// <summary>
/// The lifecycle of a queued command.
/// </summary>
public enum QueuedCommandStage
{
    Waiting = 0,
    Running,
    Retrying,
    // Succeeded, but its effect is still shown: the server data reflecting it hasn't arrived yet
    Settled,
    Failed,
}

public sealed record QueuedCommandEntry(
    string PartitionKey,
    IQueuedCommand Command,
    QueuedCommandStage Stage,
    int TryIndex,
    Exception? Error,
    Moment UpdatedAt);
