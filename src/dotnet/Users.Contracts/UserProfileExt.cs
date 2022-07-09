using System.Security;

namespace ActualChat.Users;

public static class UserProfileExt
{
    public static bool IsActive(this UserProfile? userProfile)
        => userProfile?.Status == UserStatus.Active;

    public static UserProfile AssertAuthenticated(this UserProfile? userProfile)
    {
        userProfile?.User.AssertAuthenticated();
        return userProfile!;
    }

    public static UserProfile AssertActive(this UserProfile? userProfile)
    {
        userProfile = userProfile.AssertAuthenticated();
        if (!userProfile.IsActive())
            throw new SecurityException("User is either suspended or not activated yet.");
        return userProfile;
    }
}
