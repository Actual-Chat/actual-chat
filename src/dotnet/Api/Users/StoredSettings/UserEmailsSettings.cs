using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for email digest notifications.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserEmailsSettings : StoredSettings, IHasOrigin, IHasKvasKey<UserEmailsSettings>
{
    [DataMember, MemoryPackOrder(0), Key(0)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(1), Key(1)] public TimeSpan DigestTime { get; init; } = new (9, 0, 0);
    [DataMember, MemoryPackOrder(2), Key(2)] public bool IsDigestEnabled { get; init; } = true;
    // null = each chat is summarized in the language it is written in, the rest follows the app language
    [DataMember, MemoryPackOrder(3), Key(3)] public Language? DigestLanguage { get; init; }
}
