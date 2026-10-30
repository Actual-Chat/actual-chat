namespace ActualChat.UI.Blazor.Services;

public enum CommandSource
{
    Direct = 0,
    UICommander,
}

public enum CommandStage
{
    Waiting = 0,
    Running,
    Retrying,
    Completed,
    Failed,
}

public enum CommandWaitReason
{
    None = 0,
    Lane,
    Paused,
    Connection,
}

// Index is the order the commands were sent in, which is also the order a lane runs them in

public sealed record CommandEntry(
    long Index,
    ICommand Command,
    CommandSource Source,
    string? PartitionKey,
    CommandStage Stage,
    CommandWaitReason WaitReason,
    int TryIndex,
    Exception? Error,
    Moment StartedAt,
    Moment UpdatedAt)
{
    public bool IsFinished => Stage is CommandStage.Completed or CommandStage.Failed;
    public TimeSpan? Duration => IsFinished ? UpdatedAt - StartedAt : null;
}
