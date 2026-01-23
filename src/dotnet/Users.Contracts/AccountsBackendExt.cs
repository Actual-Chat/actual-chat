namespace ActualChat.Users;

public static class AccountsBackendExt
{
    public static Task<UserId?> GetIdByPhoneHash(this IAccountsBackend accountsBackend, string phoneHash, CancellationToken cancellationToken)
        => accountsBackend.GetIdByUserIdentity(UserIdentityExt.NewHashedPhoneIdentity(phoneHash), cancellationToken);

    public static Task<UserId?> GetIdByEmailHash(this IAccountsBackend accountsBackend, string emailHash, CancellationToken cancellationToken)
        => accountsBackend.GetIdByUserIdentity(UserIdentityExt.NewHashedEmailIdentity(emailHash), cancellationToken);

    public static async Task<AccountFull[]> ListChangedFull(
        this IAccountsBackend accountsBackend,
        long minVersion,
        long maxVersion,
        UserId? lastId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var userIds = await accountsBackend
            .ListChanged(minVersion, maxVersion, lastId, batchSize, cancellationToken)
            .ConfigureAwait(false);
        return userIds.Length > 0 ? await GetAccounts().ConfigureAwait(false) : [];

        async Task<AccountFull[]> GetAccounts()
        {
            var accounts = await userIds
                .Select(id => accountsBackend.Get(id, cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false);
            return accounts.SkipNullItems().ToArray();
        }
    }
}
