namespace ActualChat.Users;

public static class AccountsBackendExt
{
    public static Task<UserId> GetIdByPhoneHash(this IAccountsBackend accountsBackend, string phoneHash, CancellationToken cancellationToken)
        => accountsBackend.GetIdByUserIdentity(UserExt.ToHashedPhoneIdentity(phoneHash), cancellationToken);

    public static Task<UserId> GetIdByEmailHash(this IAccountsBackend accountsBackend, string emailHash, CancellationToken cancellationToken)
        => accountsBackend.GetIdByUserIdentity(UserExt.ToHashedEmailIdentity(emailHash), cancellationToken);

    public static async IAsyncEnumerable<ApiArray<AccountFull>> BatchChanged(
        this IAccountsBackend accountsBackend,
        long minVersion,
        long maxVersion,
        UserId lastId,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var userIds = await accountsBackend.ListChanged(minVersion,
                    maxVersion,
                    lastId,
                    batchSize,
                    cancellationToken)
                .ConfigureAwait(false);
            if (userIds.Count == 0)
                yield break;

            var accounts = await GetAccounts(userIds).ConfigureAwait(false);
            yield return accounts;

            var last = accounts[^1];
            lastId = last.Id;
            minVersion = last.Version;
        }
        yield break;

        async Task<ApiArray<AccountFull>> GetAccounts(ApiArray<UserId> userIds)
        {
            var accounts = await userIds.Select(id => accountsBackend.Get(id, cancellationToken)).Collect().ConfigureAwait(false);
            return accounts.SkipNullItems().ToApiArray();
        }
    }
}
