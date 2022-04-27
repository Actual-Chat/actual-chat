using ActualChat.Interception;

namespace ActualChat.Users;

public static class AuthExt
{
    public static ValueTask<bool> IsUserActive(this IAuth auth, Session session, CancellationToken cancellationToken)
        => ProxyExt
            .GetServices(auth)
            .GetRequiredService<IUserProfiles>()
            .IsActive(session, cancellationToken);
}
