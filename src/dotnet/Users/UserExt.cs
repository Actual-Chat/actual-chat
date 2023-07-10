namespace ActualChat.Users;

public static class UserExt
{
    public static string? GetEmail(this User user)
        => user.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email).NullIfEmpty();

    public static bool HasPhoneIdentity(this User user)
        => user.Identities.Any(x => OrdinalEquals(x.Key.Schema, Constants.Auth.Phone.SchemeName));

    public static bool HasEmailIdentity(this User user)
        => user.Identities.Select(x => x.Key.Schema).Any(Constants.Auth.EmailSchemes.Contains);
}
