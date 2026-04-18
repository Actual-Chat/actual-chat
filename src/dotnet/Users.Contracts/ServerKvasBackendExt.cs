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
}
