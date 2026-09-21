namespace ActualChat.Users;

public class Usage(IServiceProvider services) : IUsage
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IUsageBackend Backend { get; } = services.GetRequiredService<IUsageBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<UsageSummary> GetOwnSummary(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return UsageSummary.None;

        return await Backend.GetSummary(account.Id, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRebuildOwnDays(Usage_RebuildOwnDays command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        await Commander.Call(new UsageBackend_RebuildDays(account.Id), true, cancellationToken).ConfigureAwait(false);
    }
}
