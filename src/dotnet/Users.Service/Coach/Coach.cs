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
        Session session, CoachWindow window, string? language, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return CoachSummary.None with { Window = window };

        var (range, trailing) = Ranges(window);
        var days = await Backend.ListDays(account.Id, range, language, cancellationToken).ConfigureAwait(false);
        var merged = CoachDayBuilder.Merge(range.Start, days);
        CoachDay? trailingDay = null;
        if (trailing is { } t) {
            var trailingDays = await Backend.ListDays(account.Id, t, language, cancellationToken).ConfigureAwait(false);
            trailingDay = trailingDays.Count > 0 ? CoachDayBuilder.Merge(t.Start, trailingDays) : null;
        }
        var languageSettings = await ServerKvasBackend.ForUser(account.Id)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        InvalidateAtMidnight(range);
        return CoachScoring.Summarize(
            window, merged, trailingDay, Settings.Coach, language ?? languageSettings.Primary.Value);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachDay>> ListOwnDays(
        Session session, Range<Moment> dayRange, string? language, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return account.IsGuestOrNull()
            ? ApiArray<CoachDay>.Empty
            : await Backend.ListDays(account.Id, dayRange, language, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachScorePart>> ExplainOwnScore(
        Session session, string? language, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return ApiArray<CoachScorePart>.Empty;

        var (range, _) = Ranges(CoachWindow.Days7);
        var days = await Backend.ListDays(account.Id, range, language, cancellationToken).ConfigureAwait(false);
        InvalidateAtMidnight(range);
        var languageSettings = await ServerKvasBackend.ForUser(account.Id)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return CoachScoring.Explain(
            CoachDayBuilder.Merge(range.Start, days), Settings.Coach, language ?? languageSettings.Primary.Value);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachConversation>> ListOwnConversations(
        Session session, int count, string? language, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return ApiArray<CoachConversation>.Empty;

        var limited = Math.Clamp(count, 1, Settings.Coach.RecentConversations);
        return await Backend.ListConversations(account.Id, limited, language, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<CoachMetricKind?> GetOwnFocus(
        Session session, string? language, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return null;

        var settings = await ServerKvasBackend.ForUser(account.Id).UserCoachSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var languageSettings = await ServerKvasBackend.ForUser(account.Id)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var effective = language.IsNullOrEmpty() ? languageSettings.Primary.Value : language;
        var iso = Language.GetIsoCode(effective);
        if (settings.FocusByLanguage.TryGetValue(iso, out var chosen))
            return chosen;

        var summary = await GetOwnSummary(session, CoachWindow.Days7, language, cancellationToken).ConfigureAwait(false);
        return CoachFocus.Pick(summary, settings.LevelOf(effective), Settings.Coach);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachWeekDelta>> GetOwnWeekDeltas(
        Session session, string? language, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return ApiArray<CoachWeekDelta>.Empty;

        var now = Clocks.SystemClock.Now;
        var weekStart = CoachProgressBuilder.WeekStart(UsageDay.DayOf(now));
        var thisWeek = new Range<Moment>(weekStart, weekStart + TimeSpan.FromDays(7));
        var lastWeek = new Range<Moment>(weekStart - TimeSpan.FromDays(7), weekStart);
        var thisDays = await Backend.ListDays(account.Id, thisWeek, language, cancellationToken).ConfigureAwait(false);
        var lastDays = await Backend.ListDays(account.Id, lastWeek, language, cancellationToken).ConfigureAwait(false);
        var settings = await ServerKvasBackend.ForUser(account.Id).UserCoachSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        InvalidateAtMidnight(thisWeek);
        return CoachProgressBuilder.WeekDeltas(
            CoachDayBuilder.Merge(weekStart, thisDays),
            CoachDayBuilder.Merge(lastWeek.Start, lastDays),
            settings.LevelOf(language),
            Settings.Coach,
            language);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachMilestone>> ListOwnMilestones(
        Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return ApiArray<CoachMilestone>.Empty;

        var range = new Range<Moment>(Moment.EpochStart, UsageDay.DayOf(Clocks.SystemClock.Now) + TimeSpan.FromDays(1));
        var days = await Backend.ListDays(account.Id, range, null, cancellationToken).ConfigureAwait(false);
        var merged = days
            .GroupBy(d => d.Day)
            .Select(g => CoachDayBuilder.Merge(g.Key, g))
            .ToList();
        return CoachProgressBuilder.Milestones(merged, Settings.Coach, null);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachWeekScore>> ListOwnWeekScores(
        Session session, int weeks, string? language, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return ApiArray<CoachWeekScore>.Empty;

        var count = Math.Clamp(weeks, 1, 26);
        var now = Clocks.SystemClock.Now;
        var thisWeek = CoachProgressBuilder.WeekStart(UsageDay.DayOf(now));
        var range = new Range<Moment>(thisWeek - TimeSpan.FromDays(7 * (count - 1)), thisWeek + TimeSpan.FromDays(7));
        var days = await Backend.ListDays(account.Id, range, language, cancellationToken).ConfigureAwait(false);
        InvalidateAtMidnight(range);
        return CoachProgressBuilder.WeeklyScores(days, count, now, Settings.Coach, language);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachLanguageInfo>> ListOwnLanguages(
        Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return ApiArray<CoachLanguageInfo>.Empty;

        var kvas = ServerKvasBackend.ForUser(account.Id);
        var languageSettings = await kvas.UserLanguageSettings().Get(cancellationToken).ConfigureAwait(false);
        var settings = await kvas.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
        var today = UsageDay.DayOf(Clocks.SystemClock.Now);
        var range = new Range<Moment>(today - TimeSpan.FromDays(29), today + TimeSpan.FromDays(1));
        var days = await Backend.ListDays(account.Id, range, null, cancellationToken).ConfigureAwait(false);
        InvalidateAtMidnight(range);
        return languageSettings.ListSpoken()
            .Select(l => Language.GetIsoCode(l.Value))
            .Distinct()
            .Select(iso => new CoachLanguageInfo(
                iso,
                settings.LevelOf(iso),
                days.Where(d => d.Language == iso).Sum(d => d.Words),
                SpeechTextStats.IsWordSplittable(iso)))
            .ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task OnSetFocus(Coach_SetFocus command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        var iso = Language.GetIsoCode(command.Language);
        await ServerKvasBackend.ForUser(account.Id, isOutermost: true).UserCoachSettings()
            .Update(x => x with { FocusByLanguage = command.Kind is { } kind
                ? With(x.FocusByLanguage, iso, kind)
                : Without(x.FocusByLanguage, iso) }, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnSetLanguageLevel(Coach_SetLanguageLevel command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        var iso = Language.GetIsoCode(command.Language);
        await ServerKvasBackend.ForUser(account.Id, isOutermost: true).UserCoachSettings()
            .Update(x => x with { Languages = With(x.Languages, iso, command.Level) }, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnSetChatCoaching(Coach_SetChatCoaching command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        var kvas = ServerKvasBackend.ForUser(account.Id, isOutermost: true);
        await kvas.ChatUserSettings(command.ChatId)
            .Update(x => x with { IsCoachingEnabled = command.IsEnabled }, cancellationToken)
            .ConfigureAwait(false);
        await kvas.UserCoachSettings()
            .Update(x => x with {
                // Switching a chat on is asking for coaching, so it cannot stay behind a switched-off master toggle
                IsCoachingEnabled = x.IsCoachingEnabled || command.IsEnabled == true,
                SwitchedOff = command.IsEnabled == false
                    ? x.SwitchedOff.Where(id => id != command.ChatId).Append(command.ChatId).ToApiArray()
                    : x.SwitchedOff.Where(id => id != command.ChatId).ToApiArray(),
            }, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnExcludeConversation(
        Coach_ExcludeConversation command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        var backendCommand = new CoachBackend_SetConversationExcluded(
            account.Id, command.ChatId, command.StartEntryLid, command.Language, command.IsExcluded);
        await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDeleteOwnData(Coach_DeleteOwnData command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        // The chat side goes first: its rows are what would refill the log on the next re-analysis
        await Commander
            .Call(new CoachAnalysisBackend_DeleteUserData(account.Id), true, cancellationToken)
            .ConfigureAwait(false);
        await Commander.Call(new CoachBackend_DeleteUserData(account.Id), true, cancellationToken).ConfigureAwait(false);
        var kvas = ServerKvasBackend.ForUser(account.Id, isOutermost: true);
        await kvas.UserCoachTip().Set(new UserCoachTip(), cancellationToken).ConfigureAwait(false);
        await kvas.UserCoachWeeklyNote().Set(new UserCoachWeeklyNote(), cancellationToken).ConfigureAwait(false);
        await kvas.UserCoachSettings()
            .Update(x => x with {
                Languages = new (),
                FocusByLanguage = new (),
                SelectedLanguage = "",
            }, cancellationToken)
            .ConfigureAwait(false);
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
        InvalidateAtMidnight(range);
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

    private static ApiMap<string, T> With<T>(ApiMap<string, T> map, string key, T value)
    {
        var copy = map.ToDictionary(x => x.Key, x => x.Value);
        copy[key] = value;
        return new ApiMap<string, T>(copy);
    }

    private static ApiMap<string, T> Without<T>(ApiMap<string, T> map, string key)
        => new (map.Where(x => x.Key != key).ToDictionary(x => x.Key, x => x.Value));

    // The window slides at UTC midnight even when no coach event invalidates the user's days
    private void InvalidateAtMidnight(Range<Moment> window)
    {
        var delay = window.End - Clocks.SystemClock.Now;
        if (delay > TimeSpan.Zero)
            Computed.GetCurrent().Invalidate(delay);
    }

    // "Today" is the UTC day; the client's local day is a later refinement
    private (Range<Moment> Window, Range<Moment>? Trailing) Ranges(CoachWindow window)
    {
        var today = UsageDay.DayOf(Clocks.SystemClock.Now);
        var tomorrow = today + TimeSpan.FromDays(1);
        var start = window switch {
            CoachWindow.Today => today,
            CoachWindow.Week or CoachWindow.Days7 => today - TimeSpan.FromDays(6),
            CoachWindow.Month or CoachWindow.Days30 => today - TimeSpan.FromDays(29),
            _ => Moment.EpochStart,
        };
        var trailingDays = window == CoachWindow.Days7 ? 7 : Settings.Coach.TrailingDays;
        var trailing = window == CoachWindow.AllTime
            ? (Range<Moment>?)null
            : new Range<Moment>(start - TimeSpan.FromDays(trailingDays), start);
        return (new Range<Moment>(start, tomorrow), trailing);
    }
}
