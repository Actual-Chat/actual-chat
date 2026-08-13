namespace ActualChat.Chat;

/// <summary>
/// Per-chat user preferences for notifications, language, and voice mode.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
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
    [DataMember, MemoryPackOrder(0), Key(0), LegacyLanguageFormatter(false)]
    public Language? Language { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public ChatNotificationMode NotificationMode { get; init; }
    [DataMember, MemoryPackOrder(3), Key(2)] public VoiceMode VoiceMode { get; init; }
    // Write-only wire stub: old clients throw reading a nil in this slot and then drop the
    // whole blob. Remove once no installed app version reads it, then reserve — do not reuse.
    [Obsolete("2026.08: Kept only so old clients keep reading 0 instead of nil")]
    [DataMember, MemoryPackOrder(4), Key(3)]
    public int ListeningMode { get; init; }
    [DataMember, MemoryPackOrder(5), Key(4)] public bool? MustTranslate { get; init; }
    [DataMember, MemoryPackOrder(8), Key(7)] public bool? MustTranslateOwnMessages { get; init; }
    [DataMember, MemoryPackOrder(6), Key(5), LegacyLanguageFormatter(true)]
    public Language? TranslationTargetLanguage { get; init; }
    [DataMember, MemoryPackOrder(7), Key(6)] public bool? IsTranslationSubHeaderVisible { get; init; }
}
