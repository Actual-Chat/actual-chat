namespace ActualChat.Users;

public static class AccountsBackendExt
{
    public static Task<UserId> GetIdByPhoneHash(this IAccountsBackend accountsBackend, string phoneHash, CancellationToken cancellationToken)
        => accountsBackend.GetIdByUserIdentity(UserExt.ToHashedPhoneIdentity(phoneHash), cancellationToken);

    public static Task<UserId> GetIdByEmailHash(this IAccountsBackend accountsBackend, string emailHash, CancellationToken cancellationToken)
        => accountsBackend.GetIdByUserIdentity(UserExt.ToHashedEmailIdentity(emailHash), cancellationToken);

    public static async IAsyncEnumerable<ApiArray<AccountFull>> Batch(
        this IAccountsBackend accountsBackend,
        Moment minCreatedAt,
        UserId lastUserId,
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var userIds = await accountsBackend.ListIds(minCreatedAt, lastUserId, limit, cancellationToken)
                .ConfigureAwait(false);
            if (userIds.Count == 0)
                yield break;

            var accounts = await GetAccounts(userIds).ConfigureAwait(false);
            yield return accounts;

            var lastAccount = accounts[^1];
            lastUserId = lastAccount.Id;
            minCreatedAt = lastAccount.CreatedAt;
        }
        yield break;

        async Task<ApiArray<AccountFull>> GetAccounts(ApiArray<UserId> userIds)
        {
            var accounts = await userIds.Select(id => accountsBackend.Get(id, cancellationToken)).Collect().ConfigureAwait(false);
            return accounts.SkipNullItems().ToApiArray();
        }
    }

    public static async IAsyncEnumerable<ApiArray<AccountFull>> BatchUpdates(
        this IAccountsBackend accountsBackend,
        Moment maxCreatedAt,
        long minVersion,
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lastUserId = UserId.None;
        while (!cancellationToken.IsCancellationRequested) {
            var userIds = await accountsBackend.ListChanged(maxCreatedAt, minVersion, lastUserId, limit, cancellationToken)
                .ConfigureAwait(false);
            if (userIds.Count == 0)
                yield break;

            var accounts = await GetAccounts(userIds).ConfigureAwait(false);
            yield return accounts;

            var lastAccount = accounts[^1];
            minVersion = lastAccount.Version;
            lastUserId = lastAccount.Id;
        }
        yield break;

        async Task<ApiArray<AccountFull>> GetAccounts(ApiArray<UserId> userIds)
        {
            var accounts = await userIds.Select(id => accountsBackend.Get(id, cancellationToken)).Collect().ConfigureAwait(false);
            return accounts.SkipNullItems().ToApiArray();
        }
    }
}
