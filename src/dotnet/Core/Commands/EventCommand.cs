namespace ActualChat;

/// <summary>
/// Base record for event commands that can be chained together.
/// </summary>
[DataContract]
public abstract partial record EventCommand : IEventCommand
{
    [DataMember] public string ChainId { get; init; } = "";
}
