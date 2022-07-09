namespace ActualChat.Users;

public static class AuthExt
{
    public static async ValueTask<User> RequireUser(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var user = await auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return user.Required();
    }

    public static async ValueTask<UserProfile> RequireUserProfile(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var userProfiles = auth.GetServices().GetRequiredService<IUserProfiles>();
        var userProfile = await userProfiles.Require(session, cancellationToken).ConfigureAwait(false);
        return userProfile;
    }

    public static async ValueTask<User> RequireActiveUser(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var userProfiles = auth.GetServices().GetRequiredService<IUserProfiles>();
        var userProfile = await userProfiles.RequireActive(session, cancellationToken).ConfigureAwait(false);
        return userProfile.User;
    }

    public static async ValueTask<UserProfile> RequireActiveUserProfile(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var userProfiles = auth.GetServices().GetRequiredService<IUserProfiles>();
        var userProfile = await userProfiles.RequireActive(session, cancellationToken).ConfigureAwait(false);
        return userProfile;
    }

    public static async ValueTask<User> RequireAdminUser(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var userProfiles = auth.GetServices().GetRequiredService<IUserProfiles>();
        var userProfile = await userProfiles.RequireAdmin(session, cancellationToken).ConfigureAwait(false);
        return userProfile.User;
    }

    public static async ValueTask<UserProfile> RequireAdminUserProfile(this IAuth auth, Session session, CancellationToken cancellationToken)
    {
        var userProfiles = auth.GetServices().GetRequiredService<IUserProfiles>();
        return await userProfiles.RequireAdmin(session, cancellationToken).ConfigureAwait(false);
    }
}
