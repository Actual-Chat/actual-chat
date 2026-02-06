using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

/// <summary>
/// User preferences for email digest notifications.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserEmailsSettings : IHasOrigin, IHasKvasKey<UserEmailsSettings>
{
    [DataMember, MemoryPackOrder(0)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(1)] public TimeSpan DigestTime { get; init; } = new (9, 0, 0);
    [DataMember, MemoryPackOrder(2)] public bool IsDigestEnabled { get; init; } = true;
}
