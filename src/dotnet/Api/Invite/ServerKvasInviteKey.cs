namespace ActualChat.Invite;

public static class ServerKvasInviteKey
{
    public static string ForChat(ChatId chatId) => $"@Invite.Chat({chatId})";
}
