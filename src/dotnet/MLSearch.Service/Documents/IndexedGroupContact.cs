using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.MLSearch.Documents;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record IndexedGroupContact : IHasId<ChatId>, IHasRoutingKey<ChatId>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public ChatId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public PlaceId PlaceId { get; init; }
    [DataMember, MemoryPackOrder(2)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public bool IsPublic { get; init; }
}
