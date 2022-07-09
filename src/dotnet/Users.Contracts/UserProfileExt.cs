using System.Security;

namespace ActualChat.Users;

public static class UserProfileExt
{
    public static bool IsActive(this UserProfile? userProfile)
        => userProfile?.Status == UserStatus.Active;

    public static UserProfile AssertExists(this UserProfile? userProfile)
    {
        userProfile?.User.AssertNotNull();
        return userProfile!;
    }

    public static UserProfile AssertActive(this UserProfile? userProfile)
    {
        userProfile = userProfile.AssertExists();
        if (!userProfile.IsActive())
            throw new SecurityException("User is either suspended or not activated yet.");
        return userProfile;
    }
}
