using System.Security;

namespace ActualChat.Users;

public static class UserProfileExt
{
    public static UserProfile AssertAuthenticated(this UserProfile? userProfile)
    {
        userProfile?.User.AssertAuthenticated();
        return userProfile!;
    }

    public static UserProfile AssertActive(this UserProfile? userProfile)
    {
        userProfile = userProfile.AssertAuthenticated();
        if (userProfile.Status != UserStatus.Active)
            throw new SecurityException("User is either suspended or not activated yet.");
        return userProfile;
    }
}
