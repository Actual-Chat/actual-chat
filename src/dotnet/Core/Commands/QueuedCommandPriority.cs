namespace ActualChat.Commands;

public enum QueuedCommandPriority
{
    Low = -1,
    Default = 0,
    High = 100,
    Critical = 100_000,
}
