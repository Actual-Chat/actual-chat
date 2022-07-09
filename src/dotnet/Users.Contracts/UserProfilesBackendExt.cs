namespace ActualChat.Users;

public static class UserProfilesBackendExt
{
    public static async ValueTask<UserProfile> Require(this IUserProfilesBackend userProfiles, string userId, CancellationToken cancellationToken)
    {
        var userProfile = await userProfiles.Get(userId, cancellationToken).ConfigureAwait(false);
        return userProfile.Required();
    }

    public static async ValueTask<UserProfile> RequireActive(this IUserProfilesBackend userProfiles, string userId, CancellationToken cancellationToken)
    {
        var userProfile = await userProfiles.Get(userId, cancellationToken).ConfigureAwait(false);
        return userProfile.AssertActive();
    }

    public static async ValueTask<UserProfile> RequireAdmin(this IUserProfilesBackend userProfiles, string userId, CancellationToken cancellationToken)
    {
        var userProfile = await userProfiles.Get(userId, cancellationToken).ConfigureAwait(false);
        return userProfile.AssertAdmin();
    }
}
