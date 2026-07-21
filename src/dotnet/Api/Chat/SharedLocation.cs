using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// A location shared into a chat, referenced by <see cref="ChatEntry.LocationId"/>.
/// Live while <see cref="LiveUntil"/> is in the future (its <see cref="Point"/> keeps
/// updating); once it passes, the last point is frozen as a static pin.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record SharedLocation(
    [property: DataMember, MemoryPackOrder(0), Key(0)] SharedLocationId Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long Version
) : IHasId<SharedLocationId>, IHasVersion<long>
{
    [DataMember, MemoryPackOrder(2), Key(2)] public required AuthorId AuthorId { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public required GeoPoint Point { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public required Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public required Moment ModifiedAt { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)] public required TimeSpan Duration { get; init; }
    [DataMember, MemoryPackOrder(7), Key(7)] public Moment? StoppedAt { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ChatId => AuthorId.ChatId;

    // Tolerant comparison: the DB stores Duration with microsecond precision,
    // so TimeSpan.MaxValue doesn't round-trip bit-exactly.
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsUnlimited => Duration > Constants.Location.MaxFiniteDuration;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Moment LiveUntil => IsUnlimited ? Moment.MaxValue : CreatedAt + Duration;

    public bool IsLive(Moment now) => Duration > TimeSpan.Zero && StoppedAt is null && now < LiveUntil;
}

/// <summary>
/// Change to a <see cref="SharedLocation"/>: create/update carries a new <see cref="Point"/>
/// (and <see cref="LiveDuration"/> on create); a Remove change stops it (freezes the last point).
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record SharedLocationDiff : RecordDiff
{
    public static readonly Requirement<SharedLocationDiff> MustHaveCorrectDuration = Requirement.New(
        (SharedLocationDiff? d) => {
            var duration = d?.LiveDuration ?? TimeSpan.Zero;
            return duration == TimeSpan.Zero || Constants.Location.Durations.ContainsKey(duration);
        },
        new (() => StandardError.Constraint<SharedLocationDiff>("Invalid live duration")));

    [DataMember, MemoryPackOrder(0)] public GeoPoint? Point { get; init; }
    [DataMember, MemoryPackOrder(1)] public TimeSpan? LiveDuration { get; init; }
}
