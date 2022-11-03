namespace ActualChat.Invite;

public static class ServerKvasInviteKey
{
    public static string ForChat(string chatId) => $"@Invite.Chat({chatId})";
}
