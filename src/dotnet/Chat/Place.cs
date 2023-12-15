using MemoryPack;
using Stl.Fusion.Blazor;
using Stl.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByIdAndVersionParameterComparer<PlaceId, long>))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Place(
    [property: DataMember, MemoryPackOrder(0)] PlaceId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<PlaceId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public bool IsPublic { get; init; }
    [DataMember, MemoryPackOrder(5)] public MediaId MediaId { get; init; }

    // Populated only on front-end
    [DataMember, MemoryPackOrder(11)] public PlaceRules Rules { get; init; } = null!;
    [DataMember, MemoryPackOrder(12)] public Media.Media? Picture { get; init; }

    // This record relies on referential equality
    public bool Equals(Place? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record PlaceDiff : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public string? Title { get; init; }
    [DataMember, MemoryPackOrder(2)] public bool? IsPublic { get; init; }
    [DataMember, MemoryPackOrder(8)] public MediaId? MediaId { get; init; }
}
