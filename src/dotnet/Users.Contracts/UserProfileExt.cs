using System.Security;

namespace ActualChat.Users;

public static class UserProfileExt
{
    public static UserProfile MustBeAuthenticated(this UserProfile? userProfile)
    {
        userProfile?.User.MustBeAuthenticated();
        return userProfile!;
    }

    public static UserProfile MustBeActive(this UserProfile? userProfile)
    {
        userProfile = userProfile.MustBeAuthenticated();
        if (userProfile.Status != UserStatus.Active)
            throw new SecurityException("User is either suspended or not activated yet.");
        return userProfile;
    }
}
