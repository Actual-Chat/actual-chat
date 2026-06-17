namespace ActualChat.Chat;

/// <summary>
/// A snapshot of an author's live location share in a chat: their latest
/// <see cref="Point"/> plus the share's start, last-update, and expiry moments.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record LiveLocation(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId AuthorId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] GeoPoint Point,
    [property: DataMember, MemoryPackOrder(3), Key(3)] Moment StartedAt,
    [property: DataMember, MemoryPackOrder(4), Key(4)] Moment UpdatedAt,
    [property: DataMember, MemoryPackOrder(5), Key(5)] Moment ExpiresAt
);
