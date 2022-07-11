using System.Security;

namespace ActualChat.Users;

public static class UserProfileExt
{
    public static bool IsActive(this UserProfile? userProfile)
        => userProfile?.Status == UserStatus.Active;

    public static UserProfile Required(this UserProfile? userProfile)
    {
        userProfile?.User.Required();
        return userProfile!;
    }

    public static UserProfile AssertActive(this UserProfile? userProfile)
    {
        userProfile = userProfile.Required();
        if (!userProfile.IsActive())
            throw new SecurityException("User is either suspended or not activated yet.");
        return userProfile;
    }

    public static UserProfile AssertAdmin(this UserProfile? userProfile)
    {
        userProfile = userProfile.Required();
        if (!userProfile.IsAdmin)
            throw new SecurityException("Only administrators can perform this action.");
        return userProfile;
    }
}
