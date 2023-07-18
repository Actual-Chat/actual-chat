using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record SecureToken(
    [property: DataMember, MemoryPackOrder(0)] string Token,
    [property: DataMember, MemoryPackOrder(1)] Moment ExpiresAt
);
