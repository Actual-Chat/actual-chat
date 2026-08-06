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
}
