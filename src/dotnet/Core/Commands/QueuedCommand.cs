namespace ActualChat.Commands;

[DataContract]
public record QueuedCommand(
    [property: DataMember(Order = 0)]
    string Id,
    [property: DataMember(Order = 1)]
    ICommand Command) : IQueuedCommand;
