namespace ActualChat.Commands;

public enum CommandPriority
{
    Low = -1,
    Default = 0,
    High = 100,
    Critical = 100_000,
}
