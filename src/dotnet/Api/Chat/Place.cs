using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Place(
    [property: DataMember, MemoryPackOrder(0), Key(0)] PlaceId Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long Version = 0
    ) : IHasId<PlaceId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2), Key(2)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(3), Key(3)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public bool IsPublic { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public MediaId? MediaId { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)] public MediaId? BackgroundMediaId { get; init; }
    [DataMember, MemoryPackOrder(7), Key(7)] public string Description { get; init; } = "";
    [DataMember, MemoryPackOrder(14), Key(11)] public AliasId? AliasId { get; init; }

    // Populated only on front-end
    [DataMember, MemoryPackOrder(11), Key(8)] public PlaceRules Rules { get; init; } = null!;
    [DataMember, MemoryPackOrder(12), Key(9)] public Media.Media? Picture { get; init; }
    [DataMember, MemoryPackOrder(13), Key(10)] public Media.Media? Background { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public AliasInfo<PlaceId> AliasInfo => field ??= new(Id, AliasId);

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
    [DataMember, MemoryPackOrder(9)] public MediaId? BackgroundMediaId { get; init; }
    [DataMember, MemoryPackOrder(10)] public string? Description { get; init; }
    [DataMember, MemoryPackOrder(11)] public AliasId? AliasId { get; init; }
}
