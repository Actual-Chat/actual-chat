namespace ActualChat.Users;

public class RecentEntries : IRecentEntries
{
    private IAuth Auth { get; }
    private IAccounts Accounts { get; }
    private IRecentEntriesBackend Backend { get; }
    private ICommander Commander { get; }

    public RecentEntries(IAuth auth, IAccounts accounts, IRecentEntriesBackend backend, ICommander commander)
    {
        Auth = auth;
        Accounts = accounts;
        Backend = backend;
        Commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<RecentEntry>> List(
        Session session,
        RecentScope scope,
        int limit,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuest())
            return ImmutableArray<RecentEntry>.Empty;

        return await Backend.List(account.User.Id, scope, limit, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<RecentEntry?> Update(IRecentEntries.UpdateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default; // It just spawns other commands, so nothing to do here

        var (session, scope, key, moment) = command;
        var user = await Auth.GetUser(session, cancellationToken).Require().ConfigureAwait(false);
        var cmd = new IRecentEntriesBackend.UpdateCommand(scope, user.Id, key, moment);
        return await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
    }
}
