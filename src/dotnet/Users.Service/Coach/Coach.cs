using ActualChat.Chat;
using ActualChat.Users.Module;

namespace ActualChat.Users;

public class Coach(IServiceProvider services) : ICoach
{
    private const int MaxOccurrences = 20;

    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChatCoach ChatCoach { get; } = services.GetRequiredService<IChatCoach>();
    private ICoachBackend Backend { get; } = services.GetRequiredService<ICoachBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<bool> IsEnabled(Session session, CancellationToken cancellationToken)
    {
        if (!await ChatCoach.IsEnabled(session, cancellationToken).ConfigureAwait(false))
            return false;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return false;

        return Settings.Coach.Rollout == CoachRollout.Everyone
            || account.IsAdmin
            || Settings.Coach.FocusGroupEmails.Contains(account.Email, StringComparer.OrdinalIgnoreCase);
    }

    // [ComputeMethod]
    public virtual async Task<CoachSummary> GetOwnSummary(
        Session session, CoachWindow window, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return CoachSummary.None with { Window = window };

        var (range, trailing) = Ranges(window);
        var days = await Backend.ListDays(account.Id, range, cancellationToken).ConfigureAwait(false);
        var merged = CoachDayBuilder.Merge(range.Start, days);
        CoachDay? trailingDay = null;
        if (trailing is { } t) {
            var trailingDays = await Backend.ListDays(account.Id, t, cancellationToken).ConfigureAwait(false);
            trailingDay = trailingDays.Count > 0 ? CoachDayBuilder.Merge(t.Start, trailingDays) : null;
        }
        var languageSettings = await ServerKvasBackend.ForUser(account.Id)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return CoachScoring.Summarize(window, merged, trailingDay, Settings.Coach, languageSettings.Primary.Value);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachDay>> ListOwnDays(
        Session session, Range<Moment> dayRange, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return account.IsGuestOrNull()
            ? ApiArray<CoachDay>.Empty
            : await Backend.ListDays(account.Id, dayRange, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<UserCoachTip?> GetPendingTip(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return null;

        var tip = await ServerKvasBackend.ForUser(account.Id)
            .UserCoachTip()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return tip.IsPending ? tip : null;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachOccurrence>> ListOwnOccurrences(
        Session session, string word, CoachWindow window, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull() || word.IsNullOrWhiteSpace())
            return ApiArray<CoachOccurrence>.Empty;

        var (range, _) = Ranges(window);
        return await Backend
            .ListOccurrences(account.Id, word.Trim().ToLower(), range, MaxOccurrences, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDismissTip(Coach_DismissTip command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        var accessor = ServerKvasBackend.ForUser(account.Id, isOutermost: true).UserCoachTip();
        var tip = await accessor.Get(cancellationToken).ConfigureAwait(false);
        if (tip.IsPending)
            await accessor.Set(tip with { IsDismissed = true }, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRebuildOwnDays(Coach_RebuildOwnDays command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        await Commander.Call(new CoachBackend_RebuildDays(account.Id), true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    // "Today" is the UTC day; the client's local day is a later refinement
    private (Range<Moment> Window, Range<Moment>? Trailing) Ranges(CoachWindow window)
    {
        var today = UsageDay.DayOf(Clocks.SystemClock.Now);
        var tomorrow = today + TimeSpan.FromDays(1);
        var start = window switch {
            CoachWindow.Today => today,
            CoachWindow.Week => today - TimeSpan.FromDays(6),
            CoachWindow.Month => today - TimeSpan.FromDays(29),
            _ => Moment.EpochStart,
        };
        var trailing = window == CoachWindow.AllTime
            ? (Range<Moment>?)null
            : new Range<Moment>(start - TimeSpan.FromDays(Settings.Coach.TrailingDays), start);
        return (new Range<Moment>(start, tomorrow), trailing);
    }
}
