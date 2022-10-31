namespace ActualChat.Users;

public class RecentEntries : IRecentEntries
{
    private IAccounts Accounts { get; }
    private IRecentEntriesBackend Backend { get; }
    private ICommander Commander { get; }

    public RecentEntries(IAuth auth, IAccounts accounts, IRecentEntriesBackend backend, ICommander commander)
    {
        Accounts = accounts;
        Backend = backend;
        Commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<RecentEntry>> List(
        Session session,
        RecencyScope scope,
        int limit,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return ImmutableArray<RecentEntry>.Empty;

        return await Backend.List(account.User.Id, scope, limit, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<RecentEntry?> Update(IRecentEntries.UpdateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return null; // It just spawns other commands, so nothing to do here

        var (session, scope, key, moment) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        var updateCommand = new IRecentEntriesBackend.UpdateCommand(scope, account.Id, key, moment);
        return await Commander.Call(updateCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
