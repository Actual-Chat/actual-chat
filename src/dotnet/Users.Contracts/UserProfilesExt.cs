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

    public static async Task AssertCanRead(
        this IUserProfiles userProfiles,
        Session session,
        string userId,
        CancellationToken cancellationToken)
    {
        var profile = await userProfiles.Get(session, cancellationToken).Required().ConfigureAwait(false);
        if (profile.User.Id == userId)
            return;
        if (profile.Status == UserStatus.Active)
            return;
        throw new UnauthorizedAccessException("User cannot read other profiles.");
    }

    public static async Task AssertCanUpdate(
        this IUserProfiles userProfiles,
        Session session,
        UserProfile update,
        CancellationToken cancellationToken)
    {
        var profile = await userProfiles.Get(session, cancellationToken).Required().ConfigureAwait(false);
        if (profile.Id != update.Id && !profile.IsAdmin)
            throw new UnauthorizedAccessException("Users can only update their own profiles.");

        AssertCanUpdateStatus(profile, update);
    }

    private static void AssertCanUpdateStatus(UserProfile profile, UserProfile update)
    {
        if (update.Status == profile.Status)
            return;

        if (profile.IsAdmin)
            return;

        throw new UnauthorizedAccessException("User is not allowed to update status.");
    }
}
