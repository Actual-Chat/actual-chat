using Stl.Interception;

namespace ActualChat.Users;

public static class AuthExt
{
    public static async ValueTask<User> DemandUser(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var user = await auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return user.AssertAuthenticated();
    }

    public static async ValueTask<UserProfile> DemandUserProfile(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var userProfiles = auth.GetServices().GetRequiredService<IUserProfiles>();
        var userProfile = await userProfiles.Demand(session, cancellationToken).ConfigureAwait(false);
        return userProfile;
    }

    public static async ValueTask<User> DemandActiveUser(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var userProfiles = auth.GetServices().GetRequiredService<IUserProfiles>();
        var userProfile = await userProfiles.DemandActive(session, cancellationToken).ConfigureAwait(false);
        return userProfile.User;
    }

    public static async ValueTask<UserProfile> DemandActiveUserProfile(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var userProfiles = auth.GetServices().GetRequiredService<IUserProfiles>();
        var userProfile = await userProfiles.DemandActive(session, cancellationToken).ConfigureAwait(false);
        return userProfile;
    }

    public static ValueTask<bool> IsUserActive(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var userProfiles = auth.GetServices().GetRequiredService<IUserProfiles>();
        return userProfiles.IsActive(session, cancellationToken);
    }

    public static ValueTask<bool> IsAdmin(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var userProfiles = auth.GetServices().GetRequiredService<IUserProfiles>();
        return userProfiles.IsActive(session, cancellationToken);
    }
}
