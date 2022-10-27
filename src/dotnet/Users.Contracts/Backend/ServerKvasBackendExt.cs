using ActualChat.Kvas;

namespace ActualChat.Users;

public static class ServerKvasBackendExt
{
    public static IKvas GetClient(this IServerKvasBackend serverKvasBackend, Session session)
        => new ServerKvasBackendClient(serverKvasBackend, serverKvasBackend.GetSessionPrefix(session));

    public static IKvas GetClient(this IServerKvasBackend serverKvasBackend, Account account)
        => new ServerKvasBackendClient(serverKvasBackend, serverKvasBackend.GetUserPrefix(account.Id));

    public static IKvas GetClient(this IServerKvasBackend serverKvasBackend, string prefix)
        => new ServerKvasBackendClient(serverKvasBackend, prefix);
}
