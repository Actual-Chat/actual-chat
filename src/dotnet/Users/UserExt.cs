using System.Security.Claims;

namespace ActualChat.Users;

public static class UserExt
{
    public static string? GetEmail(this User user)
        => user.Claims.GetValueOrDefault(ClaimTypes.Email).NullIfEmpty();

    public static bool HasPhoneIdentity(this User user)
        => user.GetPhoneIdentity().IsValid;

    public static bool IsPhoneVerified(this User user, Phone phone)
        => !phone.IsNone && new Phone(user.GetPhoneIdentity().SchemaBoundId) == phone;

    public static bool HasEmailIdentity(this User user)
        => user.Identities.Select(x => x.Key.Schema).Any(Constants.Auth.EmailSchemes.Contains);

    public static User WithPhone(this User user, Phone phone)
        => user.WithoutIdentity(user.GetPhoneIdentity())
            .WithIdentity(new UserIdentity(Constants.Auth.Phone.SchemeName, phone.Value))
            .WithClaim(ClaimTypes.MobilePhone, phone);

    public static UserIdentity GetPhoneIdentity(this User user)
        => user.Identities.FirstOrDefault(x => OrdinalEquals(x.Key.Schema, Constants.Auth.Phone.SchemeName)).Key;

    public static User WithoutIdentity(this User user, UserIdentity toRemove)
        => user with { Identities = user.Identities.Remove(toRemove) };
}
