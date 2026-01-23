namespace ActualChat.Users;

public static class AccountExt
{
    public static bool IsGuestOrNull([NotNullWhen(false)] this Account? account)
        => account is null || account.IsGuest;
}
