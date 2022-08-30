using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Users;

public static class AccountExt
{
    public static bool IsActive([NotNullWhen(true)] this Account? account)
        => account is { Status: AccountStatus.Active };

    public static bool IsGuest([NotNullWhen(false)] this Account? account)
        => account == null || ReferenceEquals(account, Account.Guest);
}
