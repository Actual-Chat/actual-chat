using ActualChat.Serialization;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserChatSettings
{
    public static readonly UserChatSettings Default = new();

    public static string GetKvasKey(ChatId chatId) => $"@UserChatSettings({chatId.Value})";
    public static string GetKvasKey(string chatId) => $"@UserChatSettings({chatId})";

    // `isNullable = false` is intentional to keep backward compatibility with v1.26 format when Language was non-nullable
    [DataMember, MemoryPackOrder(0)] [LanguageBackwardCompatibleFormatter(false)] public Language? Language { get; init; }
    [DataMember, MemoryPackOrder(1)] public ChatNotificationMode NotificationMode { get; init; }
    [DataMember, MemoryPackOrder(3)] public VoiceMode VoiceMode { get; init; }
    [DataMember, MemoryPackOrder(4)] public ListeningMode ListeningMode { get; init; }
    [DataMember, MemoryPackOrder(5)] public bool? MustTranslate { get; init; }
    [DataMember, MemoryPackOrder(6)] [LanguageBackwardCompatibleFormatter(true)] public Language? TranslationTargetLanguage { get; init; }
}
