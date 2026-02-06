using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Extension methods for <see cref="IServerKvasBackend"/>.
/// </summary>
public static class ServerKvasBackendExt
{
    public static IKvas<Account> GetUserClient(this IServerKvasBackend serverKvasBackend, Account account)
        => serverKvasBackend.GetUserClient(account.Id);

    public static IKvas<Account> GetUserClient(this IServerKvasBackend serverKvasBackend, UserId userId)
        => new ServerKvasBackendClient(serverKvasBackend, GetUserPrefix(userId.Require())).WithScope<Account>();

    public static IKvas GetServerSettingsClient(this IServerKvasBackend serverKvasBackend)
        => new ServerKvasBackendClient(serverKvasBackend, "srv/");

    [return: NotNullIfNotNull(nameof(userId))]
    public static string? GetUserPrefix(UserId? userId)
        => userId is null
            ? null
            : userId.IsGuest
                ? $"g/{userId}/"
                : $"u/{userId}/";
}
