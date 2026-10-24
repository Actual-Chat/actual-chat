namespace ActualChat.Chat;

/// <summary>
/// Messages a user pinned in a chat for themselves only; shown together with the chat's
/// shared <see cref="ChatPinnedEntries"/>. Ordered by message chronology, capped at <see cref="MaxCount"/>.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatUserPinnedEntries : StoredSettings
{
    public const int MaxCount = 3;
    public static readonly string KeyPrefix = "@UserPinnedEntries(";
    public static readonly string KeySuffix = ")";

    [DataMember, Key(0)] public ApiArray<ChatEntryId> EntryIds { get; init; }

    public static string GetKey(ChatId chatId) => $"{KeyPrefix}{chatId.Value}{KeySuffix}";

    public override void ValidateKey(string key)
    {
        if (!key.StartsWith(KeyPrefix) || !key.EndsWith(KeySuffix))
            throw StandardError.Constraint("Invalid key.");

        var chatIdValue = key[KeyPrefix.Length..^KeySuffix.Length];
        ChatId.Parse(chatIdValue);
    }
}
