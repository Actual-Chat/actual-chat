namespace ActualChat.Chat;

/// <summary>
/// Per-chat user preferences for notifications, language, and voice mode.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record ChatUserSettings : StoredSettings
{
    public static readonly string KeyPrefix = "@UserChatSettings(";
    public static readonly string KeySuffix = ")";
    public static readonly ChatUserSettings Default = new();

    public static string GetKey(ChatId chatId) => $"{KeyPrefix}{chatId.Value}{KeySuffix}";
    public static string GetKey(string chatId) => $"{KeyPrefix}{chatId}{KeySuffix}";

    public override void ValidateKey(string key)
    {
        if (!key.StartsWith(KeyPrefix) || !key.EndsWith(KeySuffix))
            throw StandardError.Constraint("Invalid key.");
        var chatIdValue = key[KeyPrefix.Length..^KeySuffix.Length];
        ChatId.Parse(chatIdValue);
    }

    // `isNullable = false` is intentional to keep backward compatibility with v1.26 format when Language was non-nullable
    [DataMember, MemoryPackOrder(0), LegacyLanguageFormatter(false)]
    public Language? Language { get; init; }
    [DataMember, MemoryPackOrder(1)] public ChatNotificationMode NotificationMode { get; init; }
    [DataMember, MemoryPackOrder(3)] public VoiceMode VoiceMode { get; init; }
    [DataMember, MemoryPackOrder(4)] public ListeningMode ListeningMode { get; init; }
    [DataMember, MemoryPackOrder(5)] public bool? MustTranslate { get; init; }
    [DataMember, MemoryPackOrder(8)] public bool? MustTranslateOwnMessages { get; init; }
    [DataMember, MemoryPackOrder(6), LegacyLanguageFormatter(true)]
    public Language? TranslationTargetLanguage { get; init; }
    [DataMember, MemoryPackOrder(7)] public bool? IsTranslationSubHeaderVisible { get; init; }
}
