namespace ActualChat.Users;

[DataContract]
public sealed record UserChatSettings
{
    public static UserChatSettings Default { get; } = new();

    public static string GetKvasKey(string chatId) => $"@UserChatSettings({chatId})";

    [DataMember] public Language Language { get; init; }
    [DataMember] public ChatNotificationMode NotificationMode { get; init; }
}
