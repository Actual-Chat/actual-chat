using System.Security.Claims;

namespace ActualChat.Users;

public static class UserExt
{
    public static string? GetEmail(this User user)
        => user.GetEmailIdentity().SchemaBoundId.NullIfEmpty();
    public static Phone? GetPhone(this User user)
        => Phone.TryParse(user.GetPhoneIdentity().SchemaBoundId);

    public static string? GetPhoneHash(this User user)
        => user.GetHashedPhoneIdentity().SchemaBoundId.NullIfEmpty();
    public static string? GetEmailHash(this User user)
        => user.GetHashedEmailIdentity().SchemaBoundId.NullIfEmpty();

    public static bool HasPhoneIdentity(this User user)
        => user.GetPhoneIdentity().IsValid;
    public static bool HasEmailIdentity(this User user)
        => user.GetEmailIdentity().IsValid;

    public static User WithPhone(this User user, Phone phone)
        => user.WithPhoneIdentities(phone).WithClaim(ClaimTypes.MobilePhone, phone.Value);

    public static User WithEmail(this User user, Email email)
        => user.WithEmailIdentities(email).WithClaim(ClaimTypes.Email, email.Value);

    public static User WithPhoneIdentities(this User user, Phone phone)
    {
        phone.Require();
        var phoneIdentity = user.GetPhoneIdentity();
        if (phoneIdentity != UserIdentity.None)
            throw StandardError.Constraint("Phone identity already set for this user.");

        return user.WithIdentity(ToPhoneIdentity(phone))
            .WithIdentity(ToHashedPhoneIdentity(phone.Hash))
            .WithClaim(ClaimTypes.MobilePhone, phone.Value);
    }

    public static User WithEmailIdentities(this User user, Email email)
    {
        var emailIdentity = user.GetEmailIdentity();
        if (emailIdentity != UserIdentity.None && emailIdentity.SchemaBoundId != email.Value)
            throw StandardError.Constraint("Email identity already set for this user.");

        return user.WithIdentity(ToEmailIdentity(email))
            .WithIdentity(ToHashedEmailIdentity(email.Hash));
    }

    public static UserIdentity GetPhoneIdentity(this User user)
        => user.GetIdentity(AuthSchema.Phone);
    public static UserIdentity GetEmailIdentity(this User user)
        => user.GetIdentity(AuthSchema.Email);
    public static UserIdentity GetHashedPhoneIdentity(this User user)
        => user.GetIdentity(AuthSchema.HashedPhone);
    public static UserIdentity GetHashedEmailIdentity(this User user)
        => user.GetIdentity(AuthSchema.HashedEmail);
    public static UserIdentity GetIdentity(this User user, string schema)
        => user.Identities.FirstOrDefault(x => OrdinalEquals(x.Key.Schema, schema)).Key;

    private static UserIdentity ToPhoneIdentity(Phone phone)
        => new (AuthSchema.Phone, phone.Value);
    private static UserIdentity ToEmailIdentity(Email email)
        => new (AuthSchema.Email, email.Value);
    public static UserIdentity ToHashedPhoneIdentity(string phoneHash)
        => new (AuthSchema.HashedPhone, phoneHash);
    public static UserIdentity ToHashedEmailIdentity(string emailHash)
        => new (AuthSchema.HashedEmail, emailHash);
}
