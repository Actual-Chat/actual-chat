namespace ActualChat.Users;

/// <summary>
/// Extension methods for <see cref="Account"/>.
/// </summary>
public static class AccountExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsGuestOrNull([NotNullWhen(false)] this Account? account)
        => account is null || account.IsGuest;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasId(this Account account)
        => !ReferenceEquals(account.Id, null);
}
