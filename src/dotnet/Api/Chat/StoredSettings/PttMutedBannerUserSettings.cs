namespace ActualChat.Chat;

/// <summary>
/// Remembers which mute the Push-to-talk muted banner was dismissed for, in device-local settings:
/// a mute's <c>MutedAt</c> stamp identifies it, so the next mute of the same chat shows again.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record PttMutedBannerUserSettings : StoredSettings
{
    public static readonly string KeyPrefix = "@PttMutedBanner(";
    public static readonly string KeySuffix = ")";

    public static string GetKey(ChatId chatId) => $"{KeyPrefix}{chatId.RootChatId.Value}{KeySuffix}";

    public override void ValidateKey(string key)
    {
        if (!key.StartsWith(KeyPrefix) || !key.EndsWith(KeySuffix))
            throw StandardError.Constraint("Invalid key.");
        var chatIdValue = key[KeyPrefix.Length..^KeySuffix.Length];
        ChatId.Parse(chatIdValue);
    }

    [DataMember, Key(0)] public Moment DismissedMutedAt { get; init; }
}
