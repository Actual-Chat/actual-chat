namespace ActualChat.Users;

public static class UserProfilesExt
{
    public static async ValueTask<UserProfile> Require(this IUserProfiles userProfiles, Session session, CancellationToken cancellationToken)
    {
        var userProfile = await userProfiles.Get(session, cancellationToken).ConfigureAwait(false);
        return userProfile.Required();
    }

    public static async ValueTask<UserProfile> RequireActive(this IUserProfiles userProfiles, Session session, CancellationToken cancellationToken)
    {
        var userProfile = await userProfiles.Get(session, cancellationToken).ConfigureAwait(false);
        return userProfile.AssertActive();
    }

    public static async ValueTask<UserProfile> RequireAdmin(this IUserProfiles userProfiles, Session session, CancellationToken cancellationToken)
    {
        var userProfile = await userProfiles.Get(session, cancellationToken).ConfigureAwait(false);
        return userProfile.AssertAdmin();
    }
}
