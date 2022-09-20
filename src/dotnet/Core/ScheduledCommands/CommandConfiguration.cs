namespace ActualChat.ScheduledCommands;

public interface ICommandConfiguration
{
    IBackendCommand Command { get; }
    ShardKind ShardKind { get; }
    string ShardKey { get; }
    CommandPriority Priority { get; }
}

[DataContract]
public record CommandConfiguration(
    [property: DataMember] IBackendCommand Command,
    [property: DataMember] ShardKind ShardKind = ShardKind.None,
    [property: DataMember] string ShardKey = "",
    [property: DataMember] CommandPriority Priority = CommandPriority.Normal) : ICommandConfiguration;

