namespace ActualChat.Users;

public enum ChatNotificationMode
{
    Default = 0,
    ImportantOnly = 1,
    Muted = 2,
}

[DataContract]
public record UserChatSettings
{
    public static string GetKvasKey(string chatId) => $"@UserChatSettings({chatId})";

    [DataMember] public LanguageId Language { get; init; }
    [DataMember] public ChatNotificationMode NotificationMode { get; init; }
}
