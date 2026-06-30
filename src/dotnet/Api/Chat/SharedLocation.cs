namespace ActualChat.Chat;

/// <summary>
/// A location shared into a chat, referenced by <see cref="ChatEntry.LocationId"/>.
/// Live while <see cref="LiveUntil"/> is in the future (its <see cref="Point"/> keeps
/// updating); once it passes, the last point is frozen as a static pin.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record SharedLocation(
    [property: DataMember, MemoryPackOrder(0), Key(0)] SharedLocationId Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId AuthorId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] GeoPoint Point,
    [property: DataMember, MemoryPackOrder(3), Key(3)] Moment CreatedAt,
    [property: DataMember, MemoryPackOrder(4), Key(4)] Moment ModifiedAt,
    [property: DataMember, MemoryPackOrder(5), Key(5)] TimeSpan Duration,
    [property: DataMember, MemoryPackOrder(6), Key(6)] Moment? StoppedAt = null
)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ChatId => AuthorId.ChatId;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Moment LiveUntil => CreatedAt + Duration;

    public bool IsLive(Moment now) => StoppedAt is null && now < LiveUntil;
}
