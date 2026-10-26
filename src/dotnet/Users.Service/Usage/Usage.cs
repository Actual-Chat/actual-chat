using ActualChat.Diagnostics;
using ActualChat.Users.Module;

namespace ActualChat.Users;

public class Usage(IServiceProvider services) : IUsage
{
    private const int MaxAnalyticsClientIdLength = 128;
    private static readonly TimeSpan MaxAttributionAge = TimeSpan.FromDays(90);

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
        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        await ServerKvasBackend.ForUser(account.Id).AppReviewPromptState()
            .Set(new AppReviewPromptState(), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRebuildOwnDays(Usage_RebuildOwnDays command, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        await Commander.Call(new UsageBackend_RebuildDays(account.Id), true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRecordOnboardingStep(
        Usage_RecordOnboardingStep command, CancellationToken cancellationToken)
    {
        if (!OnboardingSteps.IsValid(command.Step))
            throw StandardError.Constraint($"Unknown onboarding step: '{command.Step}'.");

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        var now = Clocks.SystemClock.Now;
        var usageEvent = UsageEventSource.OnboardingStep(command.Step, command.IsCompleted, now);
        await Commander
            .Call(new UsageBackend_Record(account.Id, ApiArray.New(usageEvent)), true, cancellationToken)
            .ConfigureAwait(false);
        if (command.Step == OnboardingSteps.Finished && command.IsCompleted) {
            var reachCmd = new UserMilestonesBackend_Reach(account.Id, MilestoneKind.Onboarded, now);
            await Commander.Call(reachCmd, true, cancellationToken).ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task OnRecordFunnelEvent(Usage_RecordFunnelEvent command, CancellationToken cancellationToken)
    {
        var funnelEvent = command.Event;
        if (!funnelEvent.IsClientReportable())
            throw StandardError.Constraint($"Funnel event '{funnelEvent}' can't be reported by a client.");

        var sessionInfo = await Accounts.GetSessionInfo(command.Session, cancellationToken).ConfigureAwait(false);
        AppKindExt.TryParseUserAgent(sessionInfo?.Description, out var appKind);
        FunnelMeters.Record(funnelEvent, appKind);
    }

    // [CommandHandler]
    public virtual async Task OnAddAttribution(Usage_AddAttribution command, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        // The capture time comes from the device clock, so it is only trusted within bounds
        var now = Clocks.SystemClock.Now;
        var seenAt = command.SeenAt.Clamp(now - MaxAttributionAge, now);
        var addCmd = new UserAttributionsBackend_Add(account.Id, command.Origin, command.Raw, seenAt);
        await Commander.Call(addCmd, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnSetAnalyticsClient(
        Usage_SetAnalyticsClient command, CancellationToken cancellationToken)
    {
        var clientId = command.ClientId;
        if (!Enum.IsDefined(command.Kind) || clientId is not { Length: > 0 and <= MaxAnalyticsClientIdLength }
            || clientId.Any(char.IsControl))
            throw StandardError.Constraint("Invalid analytics client id.");

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        var setClientCmd = new UserMilestonesBackend_SetAnalyticsClient(
            account.Id, command.Kind, clientId, Clocks.SystemClock.Now);
        await Commander.Call(setClientCmd, true, cancellationToken).ConfigureAwait(false);
    }
}
