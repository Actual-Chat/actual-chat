using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserChatSettings
{
    public static readonly UserChatSettings Default = new();

    public static string GetKvasKey(string chatId) => $"@UserChatSettings({chatId})";

    [DataMember, MemoryPackOrder(0)] public Language Language { get; init; }
    [DataMember, MemoryPackOrder(1)] public ChatNotificationMode NotificationMode { get; init; }
    [DataMember, MemoryPackOrder(2)] public bool? MustRecordVoice { get; init; }
}
