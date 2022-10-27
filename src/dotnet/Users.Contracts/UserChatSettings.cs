namespace ActualChat.Users;

[DataContract]
public record UserChatSettings
{
    internal static string GetKvasKey(string chatId) => $"@ChatUserSettings({chatId})";

    [DataMember] public LanguageId Language { get; init; }
}
