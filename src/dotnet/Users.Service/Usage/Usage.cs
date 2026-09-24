using ActualChat.Users.Module;

namespace ActualChat.Users;

public class Usage(IServiceProvider services) : IUsage
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IUsageBackend Backend { get; } = services.GetRequiredService<IUsageBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<UsageSummary> GetOwnSummary(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return UsageSummary.None;

        return await Backend.GetSummary(account.Id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<AppReviewPromptState> GetOwnReviewPromptHistory(
        Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return new AppReviewPromptState();

        return await ServerKvasBackend.ForUser(account.Id).AppReviewPromptState()
            .Get(cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<PendingReviewPrompt?> GetPendingReviewPrompt(
        Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return null;

        var history = await ServerKvasBackend.ForUser(account.Id).AppReviewPromptState()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        var ttl = Settings.ReviewPrompt.PendingTtl;
        var pending = ReviewPromptPolicy.GetPending(history, ttl, now);
        if (pending is not null)
            Computed.GetCurrent().Invalidate(pending.Since + ttl - now);
        return pending;
    }

    public virtual async Task<ReviewPromptState> GetReviewPromptState(
        Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return new(false, "Not signed in");

        return await Backend.GetReviewPromptState(account.Id, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRecordReviewPrompt(
        Usage_RecordReviewPrompt command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        var kvas = ServerKvasBackend.ForUser(account.Id).AppReviewPromptState();
        var history = await kvas.Get(cancellationToken).ConfigureAwait(false);
        var updated = ReviewPromptPolicy.Apply(history, command.Outcome, Clocks.SystemClock.Now);
        await kvas.Set(updated, cancellationToken).ConfigureAwait(false);

        var sessionInfo = await Accounts.GetSessionInfo(command.Session, cancellationToken).ConfigureAwait(false);
        AppKindExt.TryParseUserAgent(sessionInfo?.Description, out var appKind);
        UsageMeters.ReviewPromptOutcomes.Add(1,
            new KeyValuePair<string, object?>("outcome", command.Outcome.ToString()),
            new KeyValuePair<string, object?>("app", appKind.ToString()));
    }

    // [CommandHandler]
    public virtual async Task OnResetReviewPrompt(
        Usage_ResetReviewPrompt command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        await ServerKvasBackend.ForUser(account.Id).AppReviewPromptState()
            .Set(new AppReviewPromptState(), cancellationToken)
            .ConfigureAwait(false);
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
