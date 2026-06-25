namespace ActualChat.Chat;

/// <summary>
/// A location shared into a chat, referenced by <see cref="ChatEntry.LocationId"/>.
/// Live while <see cref="LiveUntil"/> is in the future (its <see cref="Point"/> keeps
/// updating); once it passes, the last point is frozen as a static pin.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record SharedLocation(
    [property: DataMember, MemoryPackOrder(0), Key(0)] SharedLocationId Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] AuthorId AuthorId,
    [property: DataMember, MemoryPackOrder(3), Key(3)] GeoPoint Point,
    [property: DataMember, MemoryPackOrder(4), Key(4)] Moment CreatedAt,
    [property: DataMember, MemoryPackOrder(5), Key(5)] Moment ModifiedAt,
    // TODO: DURATION!!!
    [property: DataMember, MemoryPackOrder(6), Key(6)] Moment LiveUntil
)
{
    public bool IsLive(Moment now) => now < LiveUntil;
}
