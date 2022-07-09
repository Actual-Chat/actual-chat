namespace ActualChat.Users;

public static class UserProfilesExt
{
    public static async ValueTask<UserProfile> Demand(this IUserProfiles userProfiles, Session session, CancellationToken cancellationToken)
    {
        var userProfile = await userProfiles.Get(session, cancellationToken).ConfigureAwait(false);
        return userProfile.AssertAuthenticated();
    }

    public static async ValueTask<UserProfile> DemandActive(this IUserProfiles userProfiles, Session session, CancellationToken cancellationToken)
    {
        var userProfile = await userProfiles.Get(session, cancellationToken).ConfigureAwait(false);
        return userProfile.AssertActive();
    }

    public static async ValueTask<bool> IsActive(this IUserProfiles userProfiles, Session session, CancellationToken cancellationToken)
    {
        var userProfile = await userProfiles.Get(session, cancellationToken).ConfigureAwait(false);
        return userProfile?.Status == UserStatus.Active;
    }

    public static async ValueTask<bool> IsAdmin(this IUserProfiles userProfiles, Session session, CancellationToken cancellationToken)
    {
        var userProfile = await userProfiles.Get(session, cancellationToken).ConfigureAwait(false);
        return userProfile?.IsAdmin == true;
    }
}
