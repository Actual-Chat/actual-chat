using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Search;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record IndexedPlaceContact : IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public PlaceId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public bool IsPublic { get; init; }
    // TODO: store Version
}
