using ActualChat.Flows;
using System.Collections.Concurrent;
using ActualChat.Chat;
using ActualChat.Db;
using ActualChat.Users.Db;
using ActualChat.Users.Flows;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

/// <summary>
/// The user side of the speech coach: an append-or-replace log of the chat-side analyses keyed by
/// source id, day rows rebuilt from it, and the live tip decided after each entry record.
/// </summary>
public class CoachBackend(IServiceProvider services)
    : ShardedDbServiceBase<UsersDbContext>(services), ICoachBackend
{
    // Occurrences are picked out of the latest entries, page by page, until enough are found or the
    // scan cap is hit
    private const int OccurrencePageSize = 100;
    private const int OccurrenceMaxRows = 5000;
    private const int ConversationEntriesPerCard = 8;
    private const int MinConversationEntries = 400;
    private static readonly TimeSpan ExclusionWindow = TimeSpan.FromDays(1);

    private static readonly TimeSpan FlowRestartDelay = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<UserId, Moment> _flowStartedAt = new();

    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private FlowHub FlowHub => field ??= Services.FlowHub();

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachDay>> ListDays(
        UserId userId, Range<Moment> dayRange, string? language, CancellationToken cancellationToken)
    {
        var days = await ListAllDays(userId, cancellationToken).ConfigureAwait(false);
        var iso = language.IsNullOrEmpty() ? null : Language.GetIsoCode(language);
        // The neutral row holds runs of days without entries in the language, so it always counts
        return days
            .Where(d => d.Day >= dayRange.Start && d.Day < dayRange.End)
            .Where(d => iso is null || d.Language == iso || d.Language == "")
            .ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachConversation>> ListConversations(
        UserId userId, int count, string? language, CancellationToken cancellationToken)
    {
        // Every write invalidates ListAllDays, so depending on it keeps this fresh
        await ListAllDays(userId, cancellationToken).ConfigureAwait(false);
        var iso = language.IsNullOrEmpty() ? null : ActualChat.Language.GetIsoCode(language);
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var runSince = (Clocks.SystemClock.Now - TimeSpan.FromDays(30)).ToDateTimeClamped();
        var take = Math.Max(count * ConversationEntriesPerCard, MinConversationEntries);
        var entries = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Kind == CoachRecordKind.Entry && !e.IsRemoved)
            .OrderByDescending(e => e.OccurredAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var isTruncated = entries.Count == take;
        var runs = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Kind == CoachRecordKind.Run && !e.IsRemoved)
            .Where(e => e.OccurredAt >= runSince)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return CoachConversationBuilder
            .Build(entries.Concat(runs).Select(e => e.ToModel()), Settings.Coach.ConversationGap, isTruncated)
            .Where(c => iso is null || c.Language == iso)
            .Take(count)
            .Select(c => CoachScoring.BandConversation(c, Settings.Coach))
            .ToApiArray();
    }

    // [ComputeMethod]
    public virtual Task<ApiArray<CoachOccurrence>> ListOccurrences(
        UserId userId, string word, Range<Moment> range, int limit, CancellationToken cancellationToken)
        => ListSkillOccurrences(userId, word, range, limit, null, null, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachOccurrence>> ListSkillOccurrences(
        UserId userId, string word, Range<Moment> range, int limit,
        string? language, CoachMetricKind? kind, CancellationToken cancellationToken)
    {
        // Every write invalidates ListAllDays, so depending on it keeps this fresh
        await ListAllDays(userId, cancellationToken).ConfigureAwait(false);
        var start = range.Start.ToDateTimeClamped();
        var end = range.End.ToDateTimeClamped();
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var found = new List<CoachOccurrence>();
        var iso = language.IsNullOrEmpty() ? null : Language.GetIsoCode(language);
        var before = end;
        var beforeSourceId = "";
        // Keyset pages: each one continues right after the last row of the previous one
        for (var scanned = 0; scanned < OccurrenceMaxRows && found.Count < limit; scanned += OccurrencePageSize) {
            var pageBefore = before;
            var pageBeforeSourceId = beforeSourceId;
            var rows = await dbContext.CoachEvents
                .Where(e => e.UserId == userId.Value && e.Kind == CoachRecordKind.Entry)
                .Where(e => !e.IsRemoved && !e.IsExcluded && e.OccurredAt >= start)
                .Where(e => e.OccurredAt < pageBefore
                    || (e.OccurredAt == pageBefore && string.Compare(e.SourceId, pageBeforeSourceId) < 0))
                .OrderByDescending(e => e.OccurredAt)
                .ThenByDescending(e => e.SourceId)
                .Take(OccurrencePageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            found.AddRange(rows
                .Select(r => r.ToModel())
                .Where(r => iso is null || r.Entry!.Language is { } l && Language.GetIsoCode(l) == iso)
                .SelectMany(r => r.Entry!.Spans
                    .Where(s => s.Word == word)
                    .Where(s => kind is null || kind == CoachMetricKind.Fillers
                        && s.Kind is SpeechSpanKind.Filler or SpeechSpanKind.FilledPause
                        || kind == CoachMetricKind.WeakWords && s.Kind == SpeechSpanKind.Weak)
                    .Select(s => new CoachOccurrence(r.ChatId, r.Entry.EntryLid, s.Start, s.Length, r.OccurredAt))));
            if (rows.Count < OccurrencePageSize)
                break;

            before = rows[^1].OccurredAt;
            beforeSourceId = rows[^1].SourceId;
        }
        return found
            .OrderByDescending(o => o.At)
            .ThenByDescending(o => o.Start)
            .Take(limit)
            .ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<CoachPaceDetails> GetPaceDetails(
        UserId userId, Range<Moment> range, string language, CancellationToken cancellationToken)
    {
        await ListAllDays(userId, cancellationToken).ConfigureAwait(false);
        var start = range.Start.ToDateTimeClamped();
        var end = range.End.ToDateTimeClamped();
        var iso = Language.GetIsoCode(language);
        var band = CoachScoring.PaceRange(Settings.Coach, iso);
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var rows = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Kind == CoachRecordKind.Entry)
            .Where(e => !e.IsRemoved && !e.IsExcluded && e.PaceData != null)
            .Where(e => e.OccurredAt >= start && e.OccurredAt < end)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.SourceId)
            .Take(OccurrenceMaxRows + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var summaries = new List<SpeechPaceSummary>();
        var moments = new List<CoachPaceMoment>();
        long below = 0, within = 0, above = 0;
        foreach (var row in rows.Take(OccurrenceMaxRows)) {
            var record = row.ToModel();
            if (record.Entry?.Language is not { } l || Language.GetIsoCode(l) != iso
                || record.Entry.Pace is not { } measurement)
                continue;

            summaries.Add(SpeechPaceSummary.FromMeasurement(measurement));
            var distribution = ActualChat.Audio.SpeechPaceHistogram.Classify(
                measurement.Analysis.Segments, band.Slow, band.Fast);
            below = checked(below + distribution.BelowMilliseconds);
            within = checked(within + distribution.WithinMilliseconds);
            above = checked(above + distribution.AboveMilliseconds);
            foreach (var segment in measurement.Analysis.Segments.Reverse()) {
                if (segment.WordsPerMinute >= band.Slow && segment.WordsPerMinute <= band.Fast
                    || moments.Count >= ICoach.MaxOccurrences)
                    continue;

                var occurrence = new CoachOccurrence(record.ChatId, record.Entry.EntryLid,
                    segment.TextRange.Start, segment.TextRange.End - segment.TextRange.Start, record.OccurredAt);
                moments.Add(new CoachPaceMoment(occurrence, segment));
            }
        }
        return new CoachPaceDetails {
            Summary = SpeechPaceSummary.Merge(summaries),
            Distribution = new ActualChat.Audio.SpeechPaceDistribution(below, within, above),
            Moments = moments.ToApiArray(),
            IsTruncated = rows.Count > OccurrenceMaxRows,
            Slow = band.Slow,
            Fast = band.Fast,
        };
    }

    // [CommandHandler]
    public virtual async Task OnRecord(CoachBackend_Record command, CancellationToken cancellationToken)
    {
        var (userId, record, isRemoved) = command;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        // One writer per user: the day is rebuilt from the log, and two concurrent rebuilds would
        // each miss the other's row
        await dbContext.CoachDays.Lock(userId.Value, cancellationToken).ConfigureAwait(false);
        var dbEvent = await dbContext.CoachEvents
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.SourceId == record.SourceId, cancellationToken)
            .ConfigureAwait(false);
        if (dbEvent is not null && record.Version < dbEvent.Version)
            return;
        if (dbEvent is { IsRemoved: true } && record.Version <= dbEvent.Version)
            return;

        var isExcluded = await IsExcludedAfter(dbContext, dbEvent, record, cancellationToken).ConfigureAwait(false);
        record = record with { IsExcluded = isExcluded };
        // A run is dated by its end, so a later analysis of a run that crossed midnight moves it to the next day
        Moment? oldDay = dbEvent is { IsRemoved: false } && dbEvent.Day != record.Day.ToDateTimeClamped()
            ? dbEvent.Day
            : null;

        if (isRemoved) {
            if (dbEvent is null) {
                dbEvent = new DbCoachEvent(record);
                dbContext.Add(dbEvent);
            }
            else if (dbEvent.IsRemoved) {
                dbEvent.Version = record.Version;
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            dbEvent.Version = record.Version;
            dbEvent.MarkRemoved();
        }
        else if (dbEvent is null)
            dbContext.Add(new DbCoachEvent(record));
        else if (!dbEvent.IsRemoved && IsSameRecord(dbEvent, record))
            return;
        else
            dbEvent.UpdateFrom(record);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RebuildDay(dbContext, userId, record.Day, cancellationToken).ConfigureAwait(false);
        if (oldDay is { } movedFrom)
            await RebuildDay(dbContext, userId, movedFrom, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => _ = ListAllDays(userId, default));
    }

    // [CommandHandler]
    public virtual async Task OnDeleteUserData(CoachBackend_DeleteUserData command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        await dbContext.CoachDays.Lock(userId.Value, cancellationToken).ConfigureAwait(false);
        await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await dbContext.CoachDays
            .Where(d => d.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        Invalidation.Defer(() => _ = ListAllDays(userId, default));
    }

    // [CommandHandler]
    public virtual async Task OnRebuildDays(CoachBackend_RebuildDays command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        await dbContext.CoachDays.Lock(userId.Value, cancellationToken).ConfigureAwait(false);
        var days = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && !e.IsRemoved && !e.IsExcluded)
            .Select(e => e.Day)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var stale = await dbContext.CoachDays.ForUpdate()
            .Where(d => d.UserId == userId.Value && !days.Contains(d.Day))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.RemoveRange(stale);
        foreach (var day in days)
            await RebuildDay(dbContext, userId, day, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => _ = ListAllDays(userId, default));
    }

    // [CommandHandler]
    public virtual async Task OnSetConversationExcluded(
        CoachBackend_SetConversationExcluded command, CancellationToken cancellationToken)
    {
        var (userId, chatId, startEntryLid, language, isExcluded) = command;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        await dbContext.CoachDays.Lock(userId.Value, cancellationToken).ConfigureAwait(false);
        var startSourceId = ChatEntryId.New(chatId, startEntryLid).Value;
        var startedAt = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.SourceId == startSourceId && !e.IsRemoved)
            .Select(e => (DateTime?)e.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (startedAt is not { } start)
            return;

        // A conversation and the runs around it lie well within a day of its first entry
        var from = start - ExclusionWindow;
        var to = start + ExclusionWindow;
        var rows = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.ChatId == chatId.Value && !e.IsRemoved)
            .Where(e => e.OccurredAt >= from && e.OccurredAt < to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var members = CoachConversationBuilder.Members(
            rows.Select(r => r.ToModel()),
            Settings.Coach.ConversationGap,
            chatId,
            startEntryLid,
            Language.GetIsoCode(language));
        var rowBySourceId = rows.ToDictionary(r => r.SourceId);
        var days = new HashSet<Moment>();
        foreach (var member in members.Where(m => m.IsExcluded != isExcluded)) {
            rowBySourceId[member.SourceId].UpdateFrom(member with { IsExcluded = isExcluded });
            days.Add(member.Day);
        }
        if (days.Count == 0)
            return;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var day in days)
            await RebuildDay(dbContext, userId, day, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => _ = ListAllDays(userId, default));
    }

    // [EventHandler]
    public virtual async Task OnCoachEntryAnalyzedEvent(
        CoachEntryAnalyzedEvent eventCommand, CancellationToken cancellationToken)
    {
        var analysis = eventCommand.Analysis;
        if (!IsTrackedUser(analysis.UserId))
            return;

        var record = CoachRecord.FromEntry(analysis);
        await Commander
            .Call(new CoachBackend_Record(analysis.UserId, record, eventCommand.IsRemoved), true, cancellationToken)
            .ConfigureAwait(false);
        if (eventCommand.IsRemoved)
            return;

        await EnsureWeeklyNoteFlow(analysis.UserId, cancellationToken).ConfigureAwait(false);
        await EvaluateTip(analysis, record, cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnCoachConversationAnalyzedEvent(
        CoachConversationAnalyzedEvent eventCommand, CancellationToken cancellationToken)
    {
        var analysis = eventCommand.Analysis;
        if (!IsTrackedUser(analysis.UserId))
            return;

        await Commander
            .Call(new CoachBackend_Record(analysis.UserId, CoachRecord.FromRun(analysis), false), true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<ApiArray<CoachDay>> ListAllDays(UserId userId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(userId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var rows = await dbContext.CoachDays
            .Where(d => d.UserId == userId.Value)
            .OrderBy(d => d.Day)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => r.ToModel()).ToApiArray();
    }

    // Private methods

    private async Task EnsureWeeklyNoteFlow(UserId userId, CancellationToken cancellationToken)
    {
        // Users who coached before the weekly note existed have no flow until something starts one; a
        // finished entry is the sign they are active, and the flow itself checks the settings
        var now = Clocks.SystemClock.Now;
        if (_flowStartedAt.TryGetValue(userId, out var at) && now - at < FlowRestartDelay)
            return;

        _flowStartedAt[userId] = now;
        try {
            await FlowHub.NewResumeEvent<CoachWeeklyNoteFlow>(userId.Value)
                .Schedule(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Starting the weekly note flow failed for {UserId}", userId);
        }
    }

    private async Task RebuildDay(
        UsersDbContext dbContext, UserId userId, Moment day, CancellationToken cancellationToken)
    {
        var dbDay = day.ToDateTimeClamped();
        var events = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Day == dbDay && !e.IsRemoved && !e.IsExcluded)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var rows = await dbContext.CoachDays.ForUpdate()
            .Where(d => d.UserId == userId.Value && d.Day == dbDay)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var models = events.Count == 0
            ? ApiArray<CoachDay>.Empty
            : CoachDayBuilder.BuildAll(day, events.Select(e => e.ToModel()), Settings.Coach.MinVocabularyWords);
        foreach (var stale in rows.Where(r => models.All(m => m.Language != r.Language)))
            dbContext.Remove(stale);
        foreach (var model in models) {
            var row = rows.FirstOrDefault(r => r.Language == model.Language);
            if (row is null) {
                row = new DbCoachDay { UserId = userId.Value, Day = dbDay, Language = model.Language };
                dbContext.Add(row);
            }
            row.UpdateFrom(model);
            row.Version = VersionGenerator.NextVersion(row.Version);
        }
    }

    private async Task EvaluateTip(CoachEntryAnalysis analysis, CoachRecord record, CancellationToken cancellationToken)
    {
        // Runs after the record command, outside any DB operation, the way the review prompt does
        try {
            var kvas = ServerKvasBackend.ForUser(analysis.UserId);
            var settings = await kvas.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
            if (settings is not { IsCoachingEnabled: true, AreLiveTipsEnabled: true }
                || settings.LevelOf(analysis.Language?.Value) == CoachLanguageLevel.Off)
                return;

            var now = Clocks.SystemClock.Now;
            var window = await ListRecentEntries(analysis.UserId, now - Settings.Coach.TipWindow, cancellationToken)
                .ConfigureAwait(false);
            var tipAccessor = kvas.UserCoachTip();
            var previous = await tipAccessor.Get(cancellationToken).ConfigureAwait(false);
            var tip = CoachTipPolicy.Evaluate(
                record,
                window,
                analysis.Spans,
                previous,
                settings,
                Settings.Coach,
                now,
                analysis.Language?.Value);
            if (tip is not null)
                await tipAccessor.Set(tip with { Origin = "" }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Tip evaluation failed for {UserId}", analysis.UserId);
        }
    }

    private async Task<List<CoachRecord>> ListRecentEntries(
        UserId userId, Moment since, CancellationToken cancellationToken)
    {
        // The latest version of each entry the user spoke since the moment given, for the tip window
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var sinceDb = since.ToDateTimeClamped();
        var rows = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Kind == CoachRecordKind.Entry && !e.IsRemoved && !e.IsExcluded)
            .Where(e => e.OccurredAt >= sinceDb)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => r.ToModel()).ToList();
    }

    private async Task<bool> IsExcludedAfter(
        UsersDbContext dbContext, DbCoachEvent? dbEvent, CoachRecord record, CancellationToken cancellationToken)
    {
        // A row keeps the user's exclusion when the chat side sends it again. A new entry that continues an
        // excluded conversation joins it, and so does the run that arrives once that conversation went quiet.
        if (dbEvent is { IsRemoved: false })
            return dbEvent.IsExcluded;

        var isRun = record.Entry is null;
        var occurredAt = record.OccurredAt.ToDateTimeClamped();
        var previous = await dbContext.CoachEvents
            .Where(e => e.UserId == record.UserId.Value && e.ChatId == record.ChatId.Value)
            .Where(e => e.Kind == CoachRecordKind.Entry && !e.IsRemoved && e.SourceId != record.SourceId)
            .Where(e => e.OccurredAt <= occurredAt)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (previous is not { IsExcluded: true })
            return false;

        Moment previousStart = previous.OccurredAt;
        var previousEnd = previousStart + TimeSpan.FromSeconds(previous.ToModel().Entry!.DurationSeconds);
        var tolerance = isRun ? CoachConversationBuilder.RunTolerance : Settings.Coach.ConversationGap;
        return record.OccurredAt - previousEnd <= tolerance;
    }

    private static bool IsSameRecord(DbCoachEvent dbEvent, CoachRecord record)
        // jsonb normalises the stored text, so equality is checked on the models
        => SystemJsonSerializer.Default.Write(dbEvent.ToModel()) == SystemJsonSerializer.Default.Write(record);

    private static bool IsTrackedUser(UserId userId)
        => userId is { IsGuest: false } && !userId.Value.IsNullOrEmpty();
}
