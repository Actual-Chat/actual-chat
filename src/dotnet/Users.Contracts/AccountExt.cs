namespace ActualChat.Users;

public static class AccountExt
{
    public static bool IsActive(this Account? account)
        => account is { Status: AccountStatus.Active };
}
