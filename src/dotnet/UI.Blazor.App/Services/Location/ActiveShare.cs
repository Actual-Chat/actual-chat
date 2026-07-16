namespace ActualChat.UI.Blazor.App.Services;

[DataContract]
[MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ActiveShare(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    // null until the first fix creates the share and posts the entry
    [property: DataMember, MemoryPackOrder(1), Key(1)] SharedLocationId? LocationId,
    // TODO: change to StartedAt
    [property: DataMember, MemoryPackOrder(2), Key(2)] Moment ExpiresAt,
    // The exact Constants.Location.Durations key the user picked — the server accepts only these
    [property: DataMember, MemoryPackOrder(3), Key(3)] TimeSpan Duration
);
