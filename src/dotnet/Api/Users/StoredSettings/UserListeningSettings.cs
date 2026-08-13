using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for how long listening continues after voice activity ends.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserListeningSettings : StoredSettings, IHasOrigin, IHasKvasKey<UserListeningSettings>
{
    // Write-only wire stub: old clients NRE on a nil in this slot. Remove once no
    // installed app version reads it, then reserve the slot — do not reuse.
    [Obsolete("2026.08: Kept only so old clients keep reading [] instead of nil")]
    [DataMember, MemoryPackOrder(0), Key(0)]
    public ChatId[] AlwaysListenedChatIds { get; init; } = [];
    [DataMember, MemoryPackOrder(1), Key(1)]
    public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(2), Key(2)]
    public ContinuedListening ContinuedListening { get; init; }
}
