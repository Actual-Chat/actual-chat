using ActualChat.Chat;
using ActualChat.Db;
using ActualChat.Users.Db;
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

    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachDay>> ListDays(
        UserId userId, Range<Moment> dayRange, CancellationToken cancellationToken)
    {
        var days = await ListAllDays(userId, cancellationToken).ConfigureAwait(false);
        return days.Where(d => d.Day >= dayRange.Start && d.Day < dayRange.End).ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachOccurrence>> ListOccurrences(
        UserId userId, string word, Range<Moment> range, int limit, CancellationToken cancellationToken)
    {
        // Every write invalidates ListAllDays, so depending on it keeps this fresh
        await ListAllDays(userId, cancellationToken).ConfigureAwait(false);
        var start = range.Start.ToDateTimeClamped();
        var end = range.End.ToDateTimeClamped();
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var found = new List<CoachOccurrence>();
        for (var skip = 0; skip < OccurrenceMaxRows && found.Count < limit; skip += OccurrencePageSize) {
            var rows = await dbContext.CoachEvents
                .Where(e => e.UserId == userId.Value && e.Kind == CoachRecordKind.Entry && !e.IsRemoved)
                .Where(e => e.OccurredAt >= start && e.OccurredAt < end)
                .OrderByDescending(e => e.OccurredAt)
                .Skip(skip)
                .Take(OccurrencePageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            found.AddRange(rows
                .Select(r => r.ToModel())
                .SelectMany(r => r.Entry!.Spans
                    .Where(s => s.Word == word)
                    .Select(s => new CoachOccurrence(r.ChatId, r.Entry.EntryLid, s.Start, s.Length, r.OccurredAt))));
            if (rows.Count < OccurrencePageSize)
                break;
        }
        return found
            .OrderByDescending(o => o.At)
            .ThenByDescending(o => o.Start)
            .Take(limit)
            .ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task OnRecord(CoachBackend_Record command, CancellationToken cancellationToken)
    {
        var (userId, record, isRemoved) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            if (context.Operation.Items.KeylessGet<bool>())
                _ = ListAllDays(userId, default);
            return;
        }

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

        if (isRemoved) {
            if (dbEvent is null) {
                dbEvent = new DbCoachEvent(record);
                dbContext.Add(dbEvent);
            }
            else if (dbEvent.IsRemoved)
                return;

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
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(true);
    }

    // [CommandHandler]
    public virtual async Task OnRebuildDays(CoachBackend_RebuildDays command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Invalidation.IsActive) {
            _ = ListAllDays(userId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        await dbContext.CoachDays.Lock(userId.Value, cancellationToken).ConfigureAwait(false);
        var days = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && !e.IsRemoved)
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
    }

    // [EventHandler]
    public virtual async Task OnCoachEntryAnalyzedEvent(
        CoachEntryAnalyzedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var analysis = eventCommand.Analysis;
        if (!IsTrackedUser(analysis.UserId))
            return;

        var record = CoachRecord.FromEntry(analysis);
        await Commander
            .Call(new CoachBackend_Record(analysis.UserId, record, eventCommand.IsRemoved), true, cancellationToken)
            .ConfigureAwait(false);
        if (eventCommand.IsRemoved)
            return;

        await EvaluateTip(analysis, record, cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnCoachConversationAnalyzedEvent(
        CoachConversationAnalyzedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

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

    private async Task RebuildDay(
        UsersDbContext dbContext, UserId userId, Moment day, CancellationToken cancellationToken)
    {
        var dbDay = day.ToDateTimeClamped();
        var events = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Day == dbDay && !e.IsRemoved)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var row = await dbContext.CoachDays.ForUpdate()
            .FirstOrDefaultAsync(d => d.UserId == userId.Value && d.Day == dbDay, cancellationToken)
            .ConfigureAwait(false);
        if (events.Count == 0) {
            if (row is not null)
                dbContext.Remove(row);
            return;
        }

        var model = CoachDayBuilder.Build(day, events.Select(e => e.ToModel()), Settings.Coach.MinVocabularyWords);
        if (row is null) {
            row = new DbCoachDay { UserId = userId.Value, Day = dbDay };
            dbContext.Add(row);
        }
        row.UpdateFrom(model);
        row.Version = VersionGenerator.NextVersion(row.Version);
    }

    // Runs after the record command, outside any DB operation, the way the review prompt does
    private async Task EvaluateTip(CoachEntryAnalysis analysis, CoachRecord record, CancellationToken cancellationToken)
    {
        try {
            var kvas = ServerKvasBackend.ForUser(analysis.UserId);
            var settings = await kvas.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
            if (settings is not { IsCoachingEnabled: true, AreLiveTipsEnabled: true })
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

    // The latest version of each entry the user spoke since the moment given, for the tip window
    private async Task<List<CoachRecord>> ListRecentEntries(UserId userId, Moment since, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var sinceDb = since.ToDateTimeClamped();
        var rows = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Kind == CoachRecordKind.Entry && !e.IsRemoved)
            .Where(e => e.OccurredAt >= sinceDb)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => r.ToModel()).ToList();
    }

    // jsonb normalises the stored text, so equality is checked on the models
    private static bool IsSameRecord(DbCoachEvent dbEvent, CoachRecord record)
        => SystemJsonSerializer.Default.Write(dbEvent.ToModel()) == SystemJsonSerializer.Default.Write(record);

    private static bool IsTrackedUser(UserId userId)
        => userId is { IsGuest: false } && !userId.Value.IsNullOrEmpty();
}
