using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// A location shared into a chat, referenced by <see cref="ChatEntry.LocationId"/>.
/// Live while <see cref="LiveUntil"/> — its expiry or the moment it was stopped, whichever
/// comes first — is in the future; its <see cref="Point"/> keeps updating until then, after
/// which the last point is frozen as a static pin. A zero <see cref="Duration"/> means it was
/// never a live share, just a pin; <see cref="IsPlace"/> tells a pin the author picked on the
/// map apart from where the author actually was.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record SharedLocation(
    [property: DataMember, Key(0)] SharedLocationId Id,
    [property: DataMember, Key(1)] long Version
) : IHasId<SharedLocationId>, IHasVersion<long>
{
    [DataMember, Key(2)] public required AuthorId AuthorId { get; init; }
    [DataMember, Key(3)] public required GeoPoint Point { get; init; }
    [DataMember, Key(4)] public required Moment CreatedAt { get; init; }
    [DataMember, Key(5)] public required Moment ModifiedAt { get; init; }
    [DataMember, Key(6)] public required TimeSpan Duration { get; init; }
    [DataMember, Key(7)] public Moment? StoppedAt { get; init; }
    [DataMember, Key(8)] public bool IsPlace { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    [IgnoreDataMember, IgnoreMember]
    public ChatId ChatId => AuthorId.ChatId;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    [IgnoreDataMember, IgnoreMember]
    public bool IsUnlimited => Duration == Constants.Location.UnlimitedDuration;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    [IgnoreDataMember, IgnoreMember]
    public Moment LiveUntil => StoppedAt ?? (IsUnlimited ? Moment.MaxValue : CreatedAt + Duration);

    public bool IsLive(Moment now) => Duration > TimeSpan.Zero && now < LiveUntil;
}

/// <summary>
/// Change to a <see cref="SharedLocation"/>: create/update carries a new <see cref="Point"/>
/// (and <see cref="LiveDuration"/> / <see cref="IsPlace"/> on create); a Remove change stops it
/// (freezes the last point).
/// </summary>
[DataContract, MessagePackObject(true)]
public sealed partial record SharedLocationDiff : RecordDiff
{
    public static readonly Requirement<SharedLocationDiff> MustHaveCorrectDuration = Requirement.New(
        (SharedLocationDiff? d) => {
            var duration = d?.LiveDuration ?? TimeSpan.Zero;
            return duration == TimeSpan.Zero || Constants.Location.Durations.Contains(duration);
        },
        new (() => StandardError.Constraint<SharedLocationDiff>("Invalid live duration")));

    [DataMember] public GeoPoint? Point { get; init; }
    [DataMember] public TimeSpan? LiveDuration { get; init; }
    [DataMember] public bool IsPlace { get; init; }
}
