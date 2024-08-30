using ActualChat.Kvas;

namespace ActualChat.Users;

public static class ServerKvasBackendExt
{
    public static IKvas<User> GetUserClient(this IServerKvasBackend serverKvasBackend, User user)
        => serverKvasBackend.GetUserClient(new UserId(user.Id));

    public static IKvas<User> GetUserClient(this IServerKvasBackend serverKvasBackend, Account account)
        => serverKvasBackend.GetUserClient(account.Id);

    public static IKvas<User> GetUserClient(this IServerKvasBackend serverKvasBackend, UserId userId)
        => new ServerKvasBackendClient(serverKvasBackend, GetUserPrefix(userId)).WithScope<User>();

    public static IKvas GetServerSettingsClient(this IServerKvasBackend serverKvasBackend)
        => new ServerKvasBackendClient(serverKvasBackend, "srv/");

    public static string GetUserPrefix(UserId userId)
        => userId.IsNone
            ? ""
            : userId.IsGuest
                ? $"g/{userId}/"
                : $"u/{userId}/";

    public static UserId GetUserIdFromPrefix(string prefix)
    {
        if (prefix.OrdinalStartsWith("g/") || prefix.OrdinalStartsWith("u/")) {
            var secondSlashIndex = prefix.OrdinalIndexOf("/", 2);
            return secondSlashIndex < 0 ? default
                : new UserId(prefix.Substring(2, secondSlashIndex - 2));
        }
        return default;
    }
}
