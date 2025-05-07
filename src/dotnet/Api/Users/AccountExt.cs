namespace ActualChat.Users;

public static class AccountExt
{
    public static bool IsGuestOrNull([NotNullWhen(false)] this Account? account)
        => account is null || account.IsGuest;

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

    public static bool HasVerifiedPhone(this AccountFull account)
        => account.Phone is { } phone && phone.IsNormalized() && account.User.GetPhone() == phone;

    public static bool HasVerifiedEmail(this AccountFull account) {
        if (account.Email.IsNullOrEmpty())
            return false;

        if (OrdinalIgnoreCaseEquals(account.User.GetEmail(), account.Email))
            return true;

        return account.IsEmailVerified;
    }

    public static string? GetVerifiedEmail(this AccountFull account)
        => account.User.GetEmail();
}
