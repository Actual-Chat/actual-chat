namespace ActualChat.Chat;

/// <summary>
/// Stores the dismissed-at timestamp for the "Allow Push to Talk" banner;
/// a dismissal older than the chat's <c>PttEnabledAt</c> epoch no longer hides the banner.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record PttAllowBannerUserSettings : StoredSettings
{
    public static readonly string KeyPrefix = "@PttAllowBanner(";
    public static readonly string KeySuffix = ")";

    public static string GetKey(ChatId chatId) => $"{KeyPrefix}{chatId.RootChatId.Value}{KeySuffix}";

    public override void ValidateKey(string key)
    {
        if (!key.StartsWith(KeyPrefix) || !key.EndsWith(KeySuffix))
            throw StandardError.Constraint("Invalid key.");
        var chatIdValue = key[KeyPrefix.Length..^KeySuffix.Length];
        ChatId.Parse(chatIdValue);
    }

    [DataMember, Key(0)] public Moment DismissedAt { get; init; }
}
