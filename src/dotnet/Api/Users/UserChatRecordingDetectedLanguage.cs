using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

/// <summary>
/// Stores the most recently detected language from a user's voice recording.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserChatRecordingDetectedLanguage : IHasKvasKey<UserChatRecordingDetectedLanguage>
{
    [DataMember, MemoryPackOrder(0)]
    public Moment Timestamp { get; init; }
    [DataMember, MemoryPackOrder(1)]
    public ChatId? ChatId { get; init; }
    [DataMember, MemoryPackOrder(2)]
    public Language? Language { get; init; }
}
