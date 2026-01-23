using System.Security.Claims;

namespace ActualChat.Users;

public static class AccountFullExt
{
    public static bool IsActive([NotNullWhen(true)] this AccountFull? account)
        => account is not null && (account.Status == AccountStatus.Active || account.IsAdmin);

    [return: NotNullIfNotNull(nameof(account))]
    public static Account? ToAccount(this AccountFull? account)
    {
        if (account == null)
            return null;

        return new Account(account.Id, account.Version) {
            Avatar = account.Avatar,
            Status = account.Status,
        };
    }

    // WithXxx

    public static AccountFull WithClaim(this AccountFull account, string name, string value)
        => account with { Claims = account.Claims.With(name, value) };

    public static AccountFull WithIdentity(this AccountFull account, UserIdentity identity, string secret = "")
        => account with { Identities = account.Identities.With(identity, secret) };

    public static AccountFull WithPhone(this AccountFull account, Phone phone)
        => account.WithClaim(ClaimTypes.MobilePhone, phone.Value) with { Phone = phone };

    public static AccountFull WithEmail(this AccountFull account, string email)
        => account with { Email = email };

    public static AccountFull WithPhoneIdentities(this AccountFull account, Phone phone)
    {
        phone.Require();
        var phoneIdentity = account.Identities.GetPhoneIdentity();
        if (phoneIdentity != UserIdentity.None)
            throw StandardError.Constraint("Cannot change phone identity - already set to a different phone number.");

        return account
            .WithIdentity(UserIdentityExt.ToPhoneIdentity(phone))
            .WithIdentity(UserIdentityExt.ToHashedPhoneIdentity(phone.Hash))
            .WithClaim(ClaimTypes.MobilePhone, phone.Value)
            .WithPhone(phone);
    }

    public static AccountFull WithEmailIdentities(this AccountFull account, Email email)
    {
        var emailIdentity = account.Identities.GetEmailIdentity();
        if (emailIdentity != UserIdentity.None && !OrdinalEquals(emailIdentity.SchemaBoundId, email.Value))
            throw StandardError.Constraint("Cannot change email identity - already set to a different email address.");

        return account
            .WithIdentity(UserIdentityExt.ToEmailIdentity(email))
            .WithIdentity(UserIdentityExt.ToHashedEmailIdentity(email.Hash))
            .WithEmail(email.Value);
    }

    // GetXxxIdentity

    public static UserIdentity GetPhoneIdentity(this AccountFull account)
        => account.Identities.GetPhoneIdentity();

    public static UserIdentity GetEmailIdentity(this AccountFull account)
        => account.Identities.GetEmailIdentity();

    public static UserIdentity GetHashedPhoneIdentity(this AccountFull account)
        => account.Identities.GetHashedPhoneIdentity();

    public static UserIdentity GetHashedEmailIdentity(this AccountFull account)
        => account.Identities.GetHashedEmailIdentity();

    public static UserIdentity GetIdentity(this AccountFull account, string schema)
        => account.Identities.GetIdentity(schema);

    // HasXxxIdentity

    public static bool HasPhoneIdentity(this AccountFull account)
        => account.Identities.HasPhoneIdentity();

    public static bool HasEmailIdentity(this AccountFull account)
        => account.Identities.HasEmailIdentity();

    // GetVerifiedXxx

    public static string? GetVerifiedEmail(this AccountFull account)
        => account.Identities.GetEmail();

    public static Phone? GetVerifiedPhone(this AccountFull account)
        => account.Identities.GetPhone();

    // HasVerifiedXxx

    public static bool HasVerifiedPhone(this AccountFull account)
        => account.Phone is { } phone && phone.IsNormalized() && account.Identities.GetPhone() == phone;

    public static bool HasVerifiedEmail(this AccountFull account) {
        if (account.Email.IsNullOrEmpty())
            return false;

        if (OrdinalIgnoreCaseEquals(account.Identities.GetEmail(), account.Email))
            return true;

        return account.IsEmailVerified;
    }
}
