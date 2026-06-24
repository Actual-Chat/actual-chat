namespace ActualChat.UI.Blazor.App.Services;

[DataContract]
[MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ActiveShare(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Moment ExpiresAt // Wall-clock (ServerClock) so it survives restarts
);
