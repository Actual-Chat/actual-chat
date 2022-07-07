using Stl.Interception;

namespace ActualChat.Users;

public static class AuthExt
{
    public static async ValueTask<User> RequireUser(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var user = await auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return user.AssertAuthenticated();
    }

    public static async ValueTask<User> RequireActiveUser(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var userProfiles = auth.GetServices().GetRequiredService<IUserProfiles>();
        var userProfile = await userProfiles.RequireActive(session, cancellationToken).ConfigureAwait(false);
        return userProfile.User;
    }

    public static ValueTask<bool> IsUserActive(this IAuth auth, Session session, CancellationToken cancellationToken)
        => ProxyExt
            .GetServices(auth)
            .GetRequiredService<IUserProfiles>()
            .IsActive(session, cancellationToken);

    public static ValueTask<bool> IsAdmin(this IAuth auth, Session session, CancellationToken cancellationToken)
        => ProxyExt
            .GetServices(auth)
            .GetRequiredService<IUserProfiles>()
            .IsAdmin(session, cancellationToken);
}
