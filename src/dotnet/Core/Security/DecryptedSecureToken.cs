namespace ActualChat.Security;

/// <summary>
/// A value with an expiration time for secure operations.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record DecryptedSecureToken(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string Value,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Moment ExpiresAt
);
