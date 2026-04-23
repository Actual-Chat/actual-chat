namespace ActualChat.Invite;

/// <summary>
/// Stores the activation key for a chat invite.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatInviteSettings : StoredSettings
{
    public static readonly string KeyPrefix = "@Invite.Chat(";
    public static readonly string KeySuffix = ")";

    public static string GetKey(ChatId chatId) => $"{KeyPrefix}{chatId}{KeySuffix}";

    public override void ValidateKey(string key)
    {
        if (!key.StartsWith(KeyPrefix) || !key.EndsWith(KeySuffix))
            throw StandardError.Constraint("Invalid key.");
        var chatIdValue = key[KeyPrefix.Length..^KeySuffix.Length];
        ChatId.Parse(chatIdValue);
    }

    [DataMember, MemoryPackOrder(0), Key(0)] public string ActivationKey { get; init; } = "";
}
