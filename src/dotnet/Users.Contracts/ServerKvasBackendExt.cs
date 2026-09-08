namespace ActualChat.Users;

public static class ServerKvasBackendExt
{
    public static UserScopedKvasBackend ForUser(
        this IServerKvasBackend serverKvasBackend,
        Account account,
        bool isOutermost = false)
        => serverKvasBackend.ForUser(account.Id, isOutermost);

    public static UserScopedKvasBackend ForUser(
        this IServerKvasBackend serverKvasBackend,
        UserId userId,
        bool isOutermost = false)
        => new(serverKvasBackend, userId, isOutermost);

    public static ChatScopedKvasBackend ForChat(
        this IServerKvasBackend serverKvasBackend,
        ChatId chatId,
        bool isOutermost = false)
        => new(serverKvasBackend, chatId, isOutermost);

    public static async Task<bool> IsPttArmed(
        this IServerKvasBackend serverKvasBackend,
        UserId userId,
        ChatId chatId,
        Moment? pttEnabledAt,
        CancellationToken cancellationToken)
    {
        // PTT is a separate opt-in from "Keep listening": waking a killed device is a
        // materially different commitment, so it gets its own chat set and its own consent -
        // valid only within the chat's current enable-epoch (pttEnabledAt).
        if (pttEnabledAt is null)
            return false;

        var settings = await serverKvasBackend.ForUser(userId).UserPttSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return settings.IsArmedIn(chatId, pttEnabledAt);
    }
}
