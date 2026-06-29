namespace ActualChat.UI.Blazor.App.Services;

[DataContract]
[MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ActiveShare(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    // null until the first fix creates the share and posts the entry
    [property: DataMember, MemoryPackOrder(1), Key(1)] SharedLocationId? LocationId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Moment ExpiresAt // Wall-clock (ServerClock) so it survives restarts
);
