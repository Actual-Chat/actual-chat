namespace ActualChat.Users;

public class RecentEntries : IRecentEntries
{
    private IAuth Auth { get; }
    private IRecentEntriesBackend Backend { get; }
    private ICommander Commander { get; }

    public RecentEntries(IAuth auth, IRecentEntriesBackend backend, ICommander commander)
    {
        Auth = auth;
        Backend = backend;
        Commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableHashSet<string>> ListUserContactIds(
        Session session,
        int limit,
        CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).Require().ConfigureAwait(false);
        return await Backend.List(user.Id, RecentScope.UserContact, limit, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<RecentEntry?> Update(IRecentEntries.UpdateUserContactCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default; // It just spawns other commands, so nothing to do here

        var (session, contactId, moment) = command;
        var user = await Auth.GetUser(session, cancellationToken).Require().ConfigureAwait(false);
        var cmd = new IRecentEntriesBackend.UpdateCommand(user.Id, RecentScope.UserContact, contactId, moment);
        return await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
    }
}
