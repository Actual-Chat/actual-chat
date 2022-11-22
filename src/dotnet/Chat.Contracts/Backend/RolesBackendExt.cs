namespace ActualChat.Chat;

public static class RolesBackendExt
{
    public static async Task<Role?> GetSystem(
        this IRolesBackend rolesBackend,
        ChatId chatId,
        SystemRole systemRole,
        CancellationToken cancellationToken)
    {
        var systemRoles = await rolesBackend.ListSystem(chatId, cancellationToken).ConfigureAwait(false);
        return systemRoles.SingleOrDefault(r => r.SystemRole == systemRole);
    }
}
