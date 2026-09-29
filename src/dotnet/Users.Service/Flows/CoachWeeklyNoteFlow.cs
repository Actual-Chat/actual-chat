using ActualChat.Flows;
using ActualChat.Users.Module;
using TimeZoneConverter;

namespace ActualChat.Users.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

// One note on Monday at the user's digest time, about the week before; it lands in the user's
// stored settings and the Progress tab shows it
[Flow(DelayQuanta = 3600)] // 1 Hour
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class CoachWeeklyNoteFlow : PeriodicFlow
{
    protected override TimeSpan MaxResumeDelay => TimeSpan.FromDays(8);

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    private UserId UserId { get; set; }
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    private TimeZoneInfo TimeZoneInfo { get; set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    private TimeSpan DeliveryTime { get; set; }

    protected override async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        var userId = UserId.Parse(Id.Arguments);
        var accounts = Services.GetRequiredService<IAccountsBackend>();
        var account = await accounts.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account?.IsGuestOrNull() != false)
            return "No account";
        if (account.TimeZone.IsNullOrEmpty() || !TZConvert.TryGetTimeZoneInfo(account.TimeZone, out var timeZoneInfo))
            return "No usable time zone";

        var kvas = Services.GetRequiredService<IServerKvasBackend>().ForUser(userId);
        var settings = await kvas.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
        if (settings.IsWeeklySummaryDisabled || !settings.IsCoachingEnabled)
            return "Weekly summary is off";

        var emails = await kvas.UserEmailsSettings().Get(cancellationToken).ConfigureAwait(false);
        UserId = userId;
        TimeZoneInfo = timeZoneInfo;
        DeliveryTime = emails.DigestTime;
        return FlowReadiness.Ready;
    }

    protected override async ValueTask<Moment> Run(CancellationToken cancellationToken)
    {
        var now = Hub.SystemNow;
        var lastDue = LastMondayAt(TimeZoneInfo, DeliveryTime, now);
        var nextRunAt = new Moment(TimeZoneInfo.ConvertTimeToUtc(
            TimeZoneInfo.ConvertTimeFromUtc(lastDue.ToDateTime(), TimeZoneInfo).AddDays(7), TimeZoneInfo));
        if (!IsDue(lastDue, LastRunAt, now))
            return nextRunAt;

        var backend = Services.GetRequiredService<ICoachBackend>();
        var settings = Services.GetRequiredService<UsersSettings>().Coach;
        var kvas = Services.GetRequiredService<IServerKvasBackend>().ForUser(UserId);
        var userSettings = await kvas.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
        var thisWeekStart = ReportedWeekStart(TimeZoneInfo, lastDue);
        var lastWeekStart = thisWeekStart - TimeSpan.FromDays(7);
        var language = userSettings.SelectedLanguage.NullIfEmpty();
        var thisDays = await backend
            .ListDays(UserId, new Range<Moment>(thisWeekStart, thisWeekStart + TimeSpan.FromDays(7)), language, cancellationToken)
            .ConfigureAwait(false);
        var lastDays = await backend
            .ListDays(UserId, new Range<Moment>(lastWeekStart, thisWeekStart), language, cancellationToken)
            .ConfigureAwait(false);
        var conversations = await backend
            .ListConversations(UserId, settings.RecentConversations, cancellationToken)
            .ConfigureAwait(false);
        var note = Compose(
            thisWeekStart,
            CoachDayBuilder.Merge(thisWeekStart, thisDays) with { Language = language ?? "" },
            CoachDayBuilder.Merge(lastWeekStart, lastDays),
            conversations,
            userSettings,
            settings);
        if (note is not null)
            await kvas.UserCoachWeeklyNote().Set(note, cancellationToken).ConfigureAwait(false);
        return nextRunAt;
    }

    public static UserCoachWeeklyNote? Compose(
        Moment weekStart,
        CoachDay thisWeek,
        CoachDay lastWeek,
        ApiArray<CoachConversation> conversations,
        UserCoachSettings settings,
        CoachScoringSettings s)
    {
        if (thisWeek.Words < s.MinScoreWords)
            return null;

        var language = thisWeek.Language.IsNullOrEmpty() ? null : thisWeek.Language;
        var score = CoachScoring.Score(thisWeek, s, language);
        var previous = CoachScoring.Score(lastWeek, s, language);
        var level = settings.LevelOf(language);
        CoachMetricKind? focus = settings.FocusByLanguage.TryGetValue(language ?? "", out var chosen)
            ? chosen
            : CoachFocus.Pick(CoachScoring.Summarize(CoachWindow.Days7, thisWeek, null, s, language), level, s);
        var deltas = CoachProgressBuilder.WeekDeltas(thisWeek, lastWeek, level, s, language);
        var focusDelta = focus is { } kind ? deltas.FirstOrDefault(d => d.Kind == kind) : null;
        var best = conversations
            .Where(c => c.StartedAt >= weekStart && c.StartedAt < weekStart + TimeSpan.FromDays(7)
                && c.SpeechSeconds >= 60)
            .OrderBy(c => (double)c.Fillers / Math.Max(1, c.Words))
            .FirstOrDefault();
        return new UserCoachWeeklyNote {
            WeekStart = weekStart,
            ScoreDelta = score is { } current && previous is { } before ? current - before : null,
            FocusKind = focus,
            FocusDelta = focusDelta is { Previous: { } was, Current: { } now } ? now - was : null,
            BestChatId = best?.ChatId ?? default!,
            BestStartLid = best?.StartEntryLid ?? 0,
        };
    }

    // The latest Monday at the delivery time, in the user's zone, that is not after now
    internal static Moment LastMondayAt(TimeZoneInfo timeZoneInfo, TimeSpan deliveryTime, Moment now)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(now.ToDateTime(), timeZoneInfo);
        var monday = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7)) + deliveryTime;
        if (monday > local)
            monday = monday.AddDays(-7);
        return new Moment(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(monday, DateTimeKind.Unspecified), timeZoneInfo));
    }

    // The note is delivered on the user's Monday, so the reported week is the one before that local
    // Monday; the day rows are UTC days, so the local date is taken as a UTC day
    internal static Moment ReportedWeekStart(TimeZoneInfo timeZoneInfo, Moment lastDue)
    {
        var localMonday = TimeZoneInfo.ConvertTimeFromUtc(lastDue.ToDateTime(), timeZoneInfo).Date;
        return new Moment(DateTime.SpecifyKind(localMonday, DateTimeKind.Utc)) - TimeSpan.FromDays(7);
    }

    internal static bool IsDue(Moment lastDue, Moment lastRunAt, Moment now)
        => lastDue <= now && (lastRunAt == default || lastRunAt < lastDue);
}
