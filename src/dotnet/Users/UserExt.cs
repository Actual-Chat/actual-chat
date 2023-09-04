using System.Security.Claims;

namespace ActualChat.Users;

public static class UserExt
{
    public static Phone GetPhone(this User user)
        => new (user.GetPhoneIdentity().SchemaBoundId);

    public static string? GetEmail(this User user)
        => user.GetEmailIdentity().SchemaBoundId.NullIfEmpty();

    public static bool HasPhoneIdentity(this User user)
        => user.GetPhoneIdentity().IsValid;

    public static bool HasEmailIdentity(this User user)
        => user.GetEmailIdentity().IsValid;

    public static User WithPhone(this User user, Phone phone)
    {
        var phoneIdentity = user.GetPhoneIdentity();
        if (phoneIdentity != UserIdentity.None)
            throw StandardError.Constraint("Phone identity already set for this user.");

        return user.WithIdentity(ToIdentity(phone))
            .WithClaim(ClaimTypes.MobilePhone, phone);
    }

    public static UserIdentity GetPhoneIdentity(this User user)
        => user.Identities.FirstOrDefault(x => OrdinalEquals(x.Key.Schema, Constants.Auth.Phone.SchemeName)).Key;

    public static UserIdentity GetEmailIdentity(this User user)
        => user.Identities.FirstOrDefault(x => OrdinalEquals(x.Key.Schema, Constants.Auth.Email.SchemeName)).Key;

    public static UserIdentity ToIdentity(this Phone phone)
        => new (Constants.Auth.Phone.SchemeName, phone.Value);
    public static UserIdentity ToEmailIdentity(string email)
        => new (Constants.Auth.Email.SchemeName, email.ToLowerInvariant());
}
