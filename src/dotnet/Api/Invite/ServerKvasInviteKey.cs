namespace ActualChat.Invite;

/// <summary>
/// Generates KVAS keys for storing invite data.
/// </summary>
public static class ServerKvasInviteKey
{
    public static string ForChat(ChatId chatId) => $"@Invite.Chat({chatId})";
}
