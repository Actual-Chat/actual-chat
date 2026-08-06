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

    public static async Task<bool> IsWalkieTalkieArmed(
        this IServerKvasBackend serverKvasBackend,
        UserId userId,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // PTT is a separate opt-in from "Keep listening": waking a killed device is a
        // materially different commitment, so it gets its own chat set and its own consent.
        var pttChatIds = await serverKvasBackend.ForUser(userId).UserWalkieTalkieSettings()
            .Get(x => x.PttChatIds, cancellationToken)
            .ConfigureAwait(false);
        return pttChatIds.Contains(chatId);
    }
}
