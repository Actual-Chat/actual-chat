using ActualChat.Kvas;

namespace ActualChat.Users;

public static class ServerKvasBackendExt
{
    public static IKvas<User> GetUserClient(this IServerKvasBackend serverKvasBackend, User user)
        => serverKvasBackend.GetUserClient(UserId.Parse(user.Id));

    public static IKvas<User> GetUserClient(this IServerKvasBackend serverKvasBackend, Account account)
        => serverKvasBackend.GetUserClient(account.Id);

    public static IKvas<User> GetUserClient(this IServerKvasBackend serverKvasBackend, UserId userId)
        => new ServerKvasBackendClient(serverKvasBackend, GetUserPrefix(userId.Require())).WithScope<User>();

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
