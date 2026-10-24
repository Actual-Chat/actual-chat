namespace ActualChat;

/// <summary>
/// The lifecycle of a queued command.
/// </summary>
public enum QueuedCommandStage
{
    Waiting = 0,
    Running,
    Retrying,
    Completed,
    Failed,
}

// Index is the enqueue order: UpdatedAt can't order a lane, since every stage change moves it
public sealed record QueuedCommandEntry(
    long Index,
    string PartitionKey,
    IQueuedCommand Command,
    QueuedCommandStage Stage,
    int TryIndex,
    Exception? Error,
    Moment UpdatedAt);
