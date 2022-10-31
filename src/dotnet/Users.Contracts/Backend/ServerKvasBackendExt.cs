using ActualChat.Kvas;

namespace ActualChat.Users;

public static class ServerKvasBackendExt
{
    public static IKvas GetUserClient(this IServerKvasBackend serverKvasBackend, User user)
        => serverKvasBackend.GetUserClient(user.Id);

    public static IKvas GetUserClient(this IServerKvasBackend serverKvasBackend, Account account)
        => serverKvasBackend.GetUserClient(account.Id);

    public static IKvas GetUserClient(this IServerKvasBackend serverKvasBackend, Symbol userId)
        => new ServerKvasBackendClient(serverKvasBackend, serverKvasBackend.GetUserPrefix(userId));

    public static IKvas GetSessionClient(this IServerKvasBackend serverKvasBackend, Session session)
        => new ServerKvasBackendClient(serverKvasBackend, serverKvasBackend.GetSessionPrefix(session));
}
