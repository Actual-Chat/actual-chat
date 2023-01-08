namespace ActualChat.Commands;

[DataContract]
public abstract record EventCommand : IEventCommand
{
    [DataMember] public Symbol ChainId { get; init; }
}
