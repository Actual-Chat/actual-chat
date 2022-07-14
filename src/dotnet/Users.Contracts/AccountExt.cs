namespace ActualChat.Users;

public static class AccountExt
{
    public static bool IsAuthenticated(this Account? account)
        => account?.User.IsAuthenticated() ?? false;

    public static bool IsGuest(this Account? account)
        => account?.User.IsGuest() ?? true;

    public static bool IsActive(this Account? account)
        => account is { Status: AccountStatus.Active };
}
