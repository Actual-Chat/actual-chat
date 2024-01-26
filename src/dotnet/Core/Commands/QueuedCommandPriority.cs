namespace ActualChat.Commands;

public enum QueuedCommandPriority: sbyte
{
    Low = -1,
    Normal = 0,
    High = 1,
    Critical = 2,
}
