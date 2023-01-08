namespace ActualChat.Commands;

[DataContract]
public record QueuedCommand(
    [property: DataMember(Order = 0)] Symbol Id,
    [property: DataMember(Order = 1)] ICommand Command,
    [property: DataMember(Order = 2)] int TryIndex = 0
);
