namespace ActualChat.Users;

public static class AccountExt
{
    public static bool IsActive(this Account? account)
        => account is { Status: AccountStatus.Active };

    public static bool IsGuest(this Account? account)
        => account == null || ReferenceEquals(account, Account.Guest);
}
