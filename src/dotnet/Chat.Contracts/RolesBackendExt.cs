namespace ActualChat.Chat;

/// <summary>
/// Extension methods for <see cref="IRolesBackend"/>.
/// </summary>
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

    public static async Task<Role> GetOrCreateSystem(
        this IRolesBackend rolesBackend,
        ICommander commander,
        ChatId chatId,
        SystemRole systemRole,
        ChatPermissions permissions,
        CancellationToken cancellationToken)
    {
        var role = await rolesBackend.GetSystem(chatId, systemRole, cancellationToken).ConfigureAwait(false);
        if (role != null)
            return role;

        var createCommand = new RolesBackend_Change(chatId, default, null, new() {
            Create = new RoleDiff {
                SystemRole = systemRole,
                Permissions = permissions,
            },
        });
        return await commander.Call(createCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<AuthorId[]> ListSystemRoleAuthorIds(
        this IRolesBackend rolesBackend,
        IChatsBackend chatsBackend,
        ChatId chatId,
        SystemRole systemRole,
        CancellationToken cancellationToken)
    {
        // Public place chats take their privileged roles from the root place chat,
        // so the resulting author ids have to be remapped back into the requested chat.
        var targetChatId = chatId;
        if (targetChatId is PlaceChatId { IsRoot: false } placeChatId) {
            var chat = await chatsBackend.Get(targetChatId, cancellationToken).ConfigureAwait(false);
            if (chat == null)
                return [];

            if (chat.IsPublic)
                targetChatId = placeChatId.PlaceId.RootChatId;
        }

        var role = await rolesBackend.GetSystem(targetChatId, systemRole, cancellationToken).ConfigureAwait(false);
        if (role == null)
            return [];

        var authorIds = await rolesBackend.ListAuthorIds(targetChatId, role.Id, cancellationToken).ConfigureAwait(false);
        if (targetChatId != chatId)
            authorIds = authorIds.Select(x => AuthorId.New(chatId, x.LocalId)).ToArray();
        return authorIds;
    }

    public static async Task<bool> IsInSystemRole(
        this IRolesBackend rolesBackend,
        IChatsBackend chatsBackend,
        AuthorId? authorId,
        SystemRole systemRole,
        CancellationToken cancellationToken)
    {
        if (authorId is null)
            return false;

        var authorIds = await rolesBackend
            .ListSystemRoleAuthorIds(chatsBackend, authorId.ChatId, systemRole, cancellationToken)
            .ConfigureAwait(false);
        return authorIds.Contains(authorId);
    }
}
