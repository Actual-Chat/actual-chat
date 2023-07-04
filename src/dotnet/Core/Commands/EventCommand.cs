using MemoryPack;

namespace ActualChat.Commands;

[DataContract]
public abstract partial record EventCommand : IEventCommand
{
    [DataMember, MemoryPackOrder(0)] public Symbol ChainId { get; init; }
}
