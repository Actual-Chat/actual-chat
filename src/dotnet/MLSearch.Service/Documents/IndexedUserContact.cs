using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.MLSearch.Documents;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record IndexedUserContact : IHasId<UserId>, IHasRoutingKey<UserId>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public UserId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string FullName { get; init; } = ""; // TODO(FC): rename to Name and update inde
    [DataMember, MemoryPackOrder(4)] public ApiArray<PlaceId> PlaceIds { get; init; }
}
