namespace ActualChat.Users;

public static class UserExt
{
    public static string? GetEmail(this User user)
        => user.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email).NullIfEmpty();
}
