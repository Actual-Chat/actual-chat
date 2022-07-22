namespace ActualChat.Chat;

public static class ChatRolesBackendExt
{
    public static async Task<ChatRole?> GetSystem(
        this IChatRolesBackend chatRolesBackend,
        string chatId,
        SystemChatRole systemRole,
        CancellationToken cancellationToken)
    {
        var systemRoles = await chatRolesBackend.ListSystem(chatId, cancellationToken).ConfigureAwait(false);
        return systemRoles.SingleOrDefault(r => r.SystemRole == systemRole);
    }
}
