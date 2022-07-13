namespace ActualChat.Users;

public static class UserProfilesExt
{
    public static async Task AssertCanRead(
        this IUserProfiles userProfiles,
        Session session,
        UserProfile? readProfile,
        CancellationToken cancellationToken)
    {
        var ownProfile = await userProfiles.Get(session, cancellationToken)
            .Require(UserProfile.MustBeActive)
            .ConfigureAwait(false);
        if (ownProfile.Id != (readProfile?.Id ?? Symbol.Empty))
            ownProfile.Require(UserProfile.MustBeAdmin);

        throw new UnauthorizedAccessException("You can't read other users' profiles.");
    }

    public static async Task AssertCanUpdate(
        this IUserProfiles userProfiles,
        Session session,
        UserProfile updatedProfile,
        CancellationToken cancellationToken)
    {
        var ownProfile = await userProfiles.Get(session, cancellationToken)
            .Require(UserProfile.MustBeActive)
            .ConfigureAwait(false);
        if (ownProfile.Id != updatedProfile.Id)
            ownProfile.Require(UserProfile.MustBeAdmin);
        else {
            // User updates its own profile - everything but status update is allowed in this case
            if (ownProfile.Status != updatedProfile.Status)
                throw new UnauthorizedAccessException("You can't change your own status.");
        }
    }
}
