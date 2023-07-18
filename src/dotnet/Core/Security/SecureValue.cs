using MemoryPack;

namespace ActualChat.Security;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record SecureValue(
    [property: DataMember, MemoryPackOrder(0)] string Value,
    [property: DataMember, MemoryPackOrder(1)] Moment ExpiresAt
);
