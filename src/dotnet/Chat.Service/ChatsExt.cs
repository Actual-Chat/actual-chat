namespace ActualChat.Chat;

public static class ChatsExt
{
    public static async ValueTask<bool> HasPermissions(
        this IChats chats,
        Session session,
        string chatId,
        ChatPermissions required,
        CancellationToken cancellationToken)
    {
        var rules = await chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        var hasPermissions = rules.Has(required);
        if (!hasPermissions && required == ChatPermissions.Read) {
            // NOTE(AY): Maybe makes sense to move this to UI code - i.e. process the invite there
            // in case there is not enough permissions & retry.
            return await chats.HasInvite(session, chatId, cancellationToken).ConfigureAwait(false);
        }
        return hasPermissions;
    }

    public static async ValueTask RequirePermissions(
        this IChats chats,
        Session session,
        string chatId,
        ChatPermissions required,
        CancellationToken cancellationToken)
    {
        if (!await chats.HasPermissions(session, chatId, required, cancellationToken).ConfigureAwait(false))
            throw ChatPermissionsExt.NotEnoughPermissions(required);
    }
}
