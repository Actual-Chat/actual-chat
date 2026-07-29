using System.Security.Claims;

namespace ActualChat.Users;

/// <summary>
/// Extension methods for <see cref="AccountFull"/>.
/// </summary>
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
        => account with { Phone = phone };

    public static AccountFull WithPhoneIdentity(this AccountFull account, Phone phone)
    {
        phone.Require();
        if (account.Identities.HasPhone(phone))
            return account; // Already has this phone identity

        return account
            .WithPhone(phone)
            .WithClaim(ClaimTypes.MobilePhone, phone.Value) with {
                Identities = account.Identities.WithPhoneIdentity(phone),
            };
    }

    public static AccountFull WithEmail(this AccountFull account, string email)
        => account with { Email = email };

    public static AccountFull WithEmailIdentity(this AccountFull account, Email email)
    {
        if (account.Identities.HasEmail(email.Value))
            return account; // Already has this email identity

        return account.WithEmail(email.Value) with {
            Identities = account.Identities.WithEmailIdentity(email),
        };
    }

    // IsXxxVerified - keep derived: Accounts_Update lets clients set Email/Phone, never Identities

    public static bool IsPhoneVerified(this AccountFull account)
        => account.Phone is { } phone && phone.IsNormalized() && account.Identities.HasPhone(phone);

    public static bool IsEmailVerified(this AccountFull account)
        => !account.Email.IsNullOrEmpty() && account.Identities.HasEmail(account.Email);
}
