using System.Security.Claims;

namespace ActualChat.Users;

public static class UserExt
{
    public static string? GetEmail(this User user)
        => user.Identities.GetEmail();
    public static Phone? GetPhone(this User user)
        => user.Identities.GetPhone();

    public static string? GetPhoneHash(this User user)
        => user.Identities.GetPhoneHash();
    public static string? GetEmailHash(this User user)
        => user.Identities.GetEmailHash();

    public static bool HasPhoneIdentity(this User user)
        => user.Identities.HasPhoneIdentity();
    public static bool HasEmailIdentity(this User user)
        => user.Identities.HasEmailIdentity();

    public static User WithPhone(this User user, Phone phone)
        => user.WithPhoneIdentities(phone).WithClaim(ClaimTypes.MobilePhone, phone.Value);

    public static User WithEmail(this User user, Email email)
        => user.WithEmailIdentities(email).WithClaim(ClaimTypes.Email, email.Value);

    public static User WithPhoneIdentities(this User user, Phone phone)
    {
        phone.Require();
        var phoneIdentity = user.Identities.GetPhoneIdentity();
        if (phoneIdentity != UserIdentity.None)
            throw StandardError.Constraint("Cannot change phone identity - already set to a different phone number.");

        return user.WithIdentity(UserIdentityExt.ToPhoneIdentity(phone))
            .WithIdentity(UserIdentityExt.ToHashedPhoneIdentity(phone.Hash))
            .WithClaim(ClaimTypes.MobilePhone, phone.Value);
    }

    public static User WithEmailIdentities(this User user, Email email)
    {
        var emailIdentity = user.Identities.GetEmailIdentity();
        if (emailIdentity != UserIdentity.None && !OrdinalEquals(emailIdentity.SchemaBoundId, email.Value))
            throw StandardError.Constraint("Cannot change email identity - already set to a different email address.");

        return user.WithIdentity(UserIdentityExt.ToEmailIdentity(email))
            .WithIdentity(UserIdentityExt.ToHashedEmailIdentity(email.Hash));
    }

    public static UserIdentity GetPhoneIdentity(this User user)
        => user.Identities.GetPhoneIdentity();
    public static UserIdentity GetEmailIdentity(this User user)
        => user.Identities.GetEmailIdentity();
    public static UserIdentity GetHashedPhoneIdentity(this User user)
        => user.Identities.GetHashedPhoneIdentity();
    public static UserIdentity GetHashedEmailIdentity(this User user)
        => user.Identities.GetHashedEmailIdentity();
    public static UserIdentity GetIdentity(this User user, string schema)
        => user.Identities.GetIdentity(schema);
}
