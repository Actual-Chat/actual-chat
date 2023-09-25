namespace ActualChat.Users;

public static class AccountsBackendExt
{
    public static Task<UserId> GetIdByPhoneHash(this IAccountsBackend accountsBackend, string phoneHash, CancellationToken cancellationToken)
        => accountsBackend.GetIdByUserIdentity(UserExt.ToHashedPhoneIdentity(phoneHash), cancellationToken);

    public static Task<UserId> GetIdByEmailHash(this IAccountsBackend accountsBackend, string emailHash, CancellationToken cancellationToken)
        => accountsBackend.GetIdByUserIdentity(UserExt.ToHashedEmailIdentity(emailHash), cancellationToken);
}
