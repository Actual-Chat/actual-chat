namespace ActualChat.Users;

public static class UserProfileExt
{
    public static bool IsActive(this UserProfile? userProfile)
        => userProfile?.Status == UserStatus.Active;
}
