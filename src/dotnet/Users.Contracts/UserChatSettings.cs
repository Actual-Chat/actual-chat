namespace ActualChat.Users;

[DataContract]
public record UserChatSettings
{
    public static string GetKvasKey(string chatId) => $"@UserChatSettings({chatId})";

    [DataMember] public LanguageId Language { get; init; }
}
