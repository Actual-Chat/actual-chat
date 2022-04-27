namespace ActualChat.Users;

public static class UserProfilesExt
{
    public static async ValueTask<bool> IsActive(this IUserProfiles userProfiles, Session session, CancellationToken cancellationToken)
    {
        var userProfile = await userProfiles.Get(session, cancellationToken).ConfigureAwait(false);
        return userProfile?.Status == UserStatus.Active;
    }
}
