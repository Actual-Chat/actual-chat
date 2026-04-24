namespace ActualChat.Chat;

/// <summary>
/// Stores the dismissed-at timestamp for the "Add Chat Members" banner.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record AddChatMembersBannerUserSettings : StoredSettings
{
    public static readonly string KeyPrefix = "@AddChatMembersBanner(";
    public static readonly string KeySuffix = ")";

    public static string GetKey(ChatId chatId) => $"{KeyPrefix}{chatId.RootChatId.Value}{KeySuffix}";

    public override void ValidateKey(string key)
    {
        if (!key.StartsWith(KeyPrefix) || !key.EndsWith(KeySuffix))
            throw StandardError.Constraint("Invalid key.");
        var chatIdValue = key[KeyPrefix.Length..^KeySuffix.Length];
        ChatId.Parse(chatIdValue);
    }

    [DataMember, MemoryPackOrder(0)] public Moment DismissedAt { get; init; }
}
