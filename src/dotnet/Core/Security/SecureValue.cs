
namespace ActualChat.Security;

/// <summary>
/// A value with an expiration time for secure operations.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record SecureValue(
    [property: DataMember, MemoryPackOrder(0)] string Value,
    [property: DataMember, MemoryPackOrder(1)] Moment ExpiresAt
);
