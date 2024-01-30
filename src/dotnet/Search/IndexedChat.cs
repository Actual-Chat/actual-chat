using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Search;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record IndexedChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0) : IHasId<ChatId>, IHasVersion<long>, IRequirementTarget
{
    [property: DataMember, MemoryPackOrder(2)] public long LastEntryLocalId { get; init; }
    [property: DataMember, MemoryPackOrder(3)] public long LastEntryVersion { get; init; }
    [property: DataMember, MemoryPackOrder(4)] public Moment ChatCreatedAt { get; init; }
}
