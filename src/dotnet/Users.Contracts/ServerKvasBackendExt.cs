namespace ActualChat.Users;

/// <summary>
/// Extension methods for <see cref="IServerKvasBackend"/>.
/// </summary>
public static class ServerKvasBackendExt
{
    public static UserScopedKvasBackend ForUser(this IServerKvasBackend serverKvasBackend, Account account)
        => serverKvasBackend.ForUser(account.Id);

    public static UserScopedKvasBackend ForUser(this IServerKvasBackend serverKvasBackend, UserId userId)
        => new(serverKvasBackend, userId);

    public static async Task<bool> IsWalkieTalkieArmed(
        this IServerKvasBackend serverKvasBackend,
        UserId userId,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var kvas = serverKvasBackend.ForUser(userId);
        var alwaysListened = await kvas.UserListeningSettings()
            .Get(x => x.AlwaysListenedChatIds, cancellationToken)
            .ConfigureAwait(false);
        if (alwaysListened.Contains(chatId))
            return true;

        var listeningMode = await kvas.ChatUserSettings(chatId)
            .Get(x => x.ListeningMode, cancellationToken)
            .ConfigureAwait(false);
        return listeningMode == ListeningMode.Forever;
    }
}
