namespace ActualChat.ScheduledCommands;

public enum CommandPriority
{
    Low = -1,
    Normal = 0,
    High = 100,
    Critical = 100_000,
}
