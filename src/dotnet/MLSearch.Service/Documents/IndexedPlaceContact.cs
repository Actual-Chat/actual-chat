using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.MLSearch.Documents;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record IndexedPlaceContact : IHasId<PlaceId>, IHasRoutingKey<PlaceId>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public PlaceId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public bool IsPublic { get; init; }
}
