using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Users;

public static class AccountExt
{
    public static bool IsActive([NotNullWhen(true)] this AccountFull? account)
        => AccountFull.MustBeActive.IsSatisfied(account);

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
}
