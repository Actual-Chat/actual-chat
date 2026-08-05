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
        // PTT is a separate opt-in from "Keep listening": waking a killed device is a
        // materially different commitment, so it gets its own chat set and its own consent.
        var pttChatIds = await serverKvasBackend.ForUser(userId).UserWalkieTalkieSettings()
            .Get(x => x.PttChatIds, cancellationToken)
            .ConfigureAwait(false);
        return pttChatIds.Contains(chatId);
    }
}
