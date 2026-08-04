using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat;

[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Place(
    [property: DataMember, Key(0)] PlaceId Id,
    [property: DataMember, Key(1)] long Version = 0
    ) : IHasId<PlaceId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public string Title { get; init; } = "";
    [DataMember, Key(3)] public Moment CreatedAt { get; init; }
    [DataMember, Key(4)] public bool IsPublic { get; init; }
    [DataMember, Key(5)] public MediaId? MediaId { get; init; }
    [DataMember, Key(6)] public MediaId? BackgroundMediaId { get; init; }
    [DataMember, Key(7)] public string Description { get; init; } = "";
    [DataMember, Key(11)] public AliasId? AliasId { get; init; }

    // Populated only on front-end
    [DataMember, Key(8)] public PlaceRules Rules { get; init; } = null!;
    [DataMember, Key(9)] public Media.Media? Picture { get; init; }
    [DataMember, Key(10)] public Media.Media? Background { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public AliasInfo<PlaceId> AliasInfo => field ??= new(Id, AliasId);

    // This record relies on referential equality
    public bool Equals(Place? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

[DataContract, MessagePackObject(true)]
public sealed partial record PlaceDiff : RecordDiff
{
    [DataMember] public string? Title { get; init; }
    [DataMember] public bool? IsPublic { get; init; }
    [DataMember] public MediaId? MediaId { get; init; }
    [DataMember] public MediaId? BackgroundMediaId { get; init; }
    [DataMember] public string? Description { get; init; }
    [DataMember] public AliasId? AliasId { get; init; }
}
