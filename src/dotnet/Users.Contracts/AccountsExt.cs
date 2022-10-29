namespace ActualChat.Users;

public static class AccountsExt
{
    public static async Task AssertCanRead(
        this IAccounts accounts,
        Session session,
        AccountFull? accessedAccount,
        CancellationToken cancellationToken)
    {
        var ownAccount = await accounts.GetOwn(session, cancellationToken)
            .Require(AccountFull.MustBeActive)
            .ConfigureAwait(false);
        if (ownAccount.Id != (accessedAccount?.Id ?? Symbol.Empty))
            ownAccount.Require(AccountFull.MustBeAdmin);

        throw StandardError.Unauthorized("You can't read accounts of other users.");
    }

    public static async Task AssertCanUpdate(
        this IAccounts accounts,
        Session session,
        AccountFull updatedAccount,
        CancellationToken cancellationToken)
    {
        var ownAccount = await accounts.GetOwn(session, cancellationToken)
            .Require(AccountFull.MustBeActive)
            .ConfigureAwait(false);
        if (ownAccount.Id != updatedAccount.Id)
            ownAccount.Require(AccountFull.MustBeAdmin);
        else {
            // User updates its own profile - everything but status update is allowed in this case
            if (ownAccount.Status != updatedAccount.Status)
                throw StandardError.Unauthorized("You can't change your own status.");
        }
    }
}
