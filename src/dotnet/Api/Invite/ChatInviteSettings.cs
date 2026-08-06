using ActualChat.Kvas;

namespace ActualChat.Invite;

/// <summary>
/// Hidden KVAS entry recording which invite granted this user access to a chat.
/// The key names the granted chat, so only the server may write it.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ChatInviteSettings : StoredSettings
{
    public static readonly string KeyPrefix = $"{KvasKeys.HiddenPrefix}Invite.Chat(";
    public static readonly string KeySuffix = ")";
    public static string GetKey(ChatId chatId) => $"{KeyPrefix}{chatId}{KeySuffix}";

    public override void ValidateKey(string key)
    {
        if (!key.StartsWith(KeyPrefix) || !key.EndsWith(KeySuffix))
            throw StandardError.Constraint("Invalid key.");

        var chatIdValue = key[KeyPrefix.Length..^KeySuffix.Length];
        ChatId.Parse(chatIdValue);
    }

    [DataMember, MemoryPackOrder(0), Key(0)] public string InviteId { get; init; } = "";
}
