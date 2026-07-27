namespace ActualChat.Users;

public static class AccountsExt
{
    public static async ValueTask<bool> IsValidSession(
        this IAccounts accounts,
        [NotNullWhen(true)] Session? session,
        CancellationToken cancellationToken)
    {
        if (session?.IsValid() != true)
            return false;

        var sessionInfo = await accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        return sessionInfo?.IsActive == true;
    }

    public static async Task AssertCanRead(
        this IAccounts accounts,
        Session session,
        AccountFull? accessedAccount,
        CancellationToken cancellationToken)
    {
        if (accessedAccount == null)
            return;

        var ownAccount = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        ownAccount.Require(AccountFull.MustBeActive);
        if (ownAccount.Id != accessedAccount.Id && !ownAccount.IsAdmin)
            throw StandardError.Unauthorized("You can't read accounts of other users.");
    }

    public static async Task AssertCanUpdate(
        this IAccounts accounts,
        Session session,
        AccountFull updatedAccount,
        CancellationToken cancellationToken)
    {
        var ownAccount = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        ownAccount.Require(AccountFull.MustBeActive);
        if (ownAccount.Id != updatedAccount.Id)
            ownAccount.Require(AccountFull.MustBeAdmin);
        else {
            // User updates its own profile
            if (ownAccount.Phone != updatedAccount.Phone) {
                if (updatedAccount.Phone?.IsNormalized() == false)
                    throw StandardError.Constraint<Phone>("Incorrect phone number format.");
            }
            if (ownAccount.Status != updatedAccount.Status)
                throw StandardError.Unauthorized("You can't change your own status.");
        }
    }
}
