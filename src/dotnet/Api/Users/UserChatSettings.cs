using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserChatSettings
{
    public const string Prefix = "@UserChatSettings";

    public static readonly UserChatSettings Default = new();
    public static string GetKvasKey(string chatId) => $"{Prefix}({chatId})";

    [DataMember, MemoryPackOrder(0)] public Language Language { get; init; }
    [DataMember, MemoryPackOrder(1)] public ChatNotificationMode NotificationMode { get; init; }
    [DataMember, MemoryPackOrder(3)] public VoiceMode VoiceMode { get; init; }
    [DataMember, MemoryPackOrder(4)] public ListeningMode ListeningMode { get; init; }
}
