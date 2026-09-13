namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record Passkey(
    [property: DataMember, Key(0)] string Id,
    [property: DataMember, Key(1)] string Name,
    [property: DataMember, Key(2)] Moment CreatedAt,
    [property: DataMember, Key(3)] Moment? LastUsedAt,
    [property: DataMember, Key(4)] bool IsSynced
);
