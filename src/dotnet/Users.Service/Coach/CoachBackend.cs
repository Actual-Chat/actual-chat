using ActualChat.Chat;
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
    // Occurrences are picked out of the latest entries; this many rows per requested occurrence
    // bounds the scan for a rare word
    private const int OccurrenceRowsPerLimit = 5;

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
        var start = range.Start.ToDateTimeClamped();
        var end = range.End.ToDateTimeClamped();
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var rows = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Kind == CoachRecordKind.Entry)
            .Where(e => e.OccurredAt >= start && e.OccurredAt < end)
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit * OccurrenceRowsPerLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Select(r => r.ToModel())
            .SelectMany(r => r.Entry!.Spans
                .Where(s => s.Word == word)
                .Select(s => new CoachOccurrence(r.ChatId, r.Entry.EntryLid, s.Start, s.Length, r.OccurredAt)))
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
        var dbEvent = await dbContext.CoachEvents.ForUpdate()
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.SourceId == record.SourceId, cancellationToken)
            .ConfigureAwait(false);
        var hasChanges = false;
        if (isRemoved) {
            if (dbEvent is not null) {
                dbContext.Remove(dbEvent);
                hasChanges = true;
            }
        }
        else if (dbEvent is null) {
            dbContext.Add(new DbCoachEvent(record));
            hasChanges = true;
        }
        else if (dbEvent.Payload != SystemJsonSerializer.Default.Write(record)) {
            dbEvent.UpdateFrom(record);
            hasChanges = true;
        }
        if (!hasChanges)
            return;

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
        var days = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value)
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
        var before = await GetDay(analysis.UserId, record.Day, cancellationToken).ConfigureAwait(false);
        await Commander
            .Call(new CoachBackend_Record(analysis.UserId, record, eventCommand.IsRemoved), true, cancellationToken)
            .ConfigureAwait(false);
        if (eventCommand.IsRemoved)
            return;

        await EvaluateTip(analysis, record, before, cancellationToken).ConfigureAwait(false);
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

    private async Task<CoachDay> GetDay(UserId userId, Moment day, CancellationToken cancellationToken)
    {
        var range = new Range<Moment>(day, day + TimeSpan.FromDays(1));
        var days = await ListDays(userId, range, cancellationToken).ConfigureAwait(false);
        return days.Count > 0 ? days[0] : new CoachDay(day);
    }

    private async Task RebuildDay(
        UsersDbContext dbContext, UserId userId, Moment day, CancellationToken cancellationToken)
    {
        var dbDay = day.ToDateTimeClamped();
        var events = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Day == dbDay)
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
    private async Task EvaluateTip(
        CoachEntryAnalysis analysis, CoachRecord record, CoachDay before, CancellationToken cancellationToken)
    {
        try {
            var kvas = ServerKvasBackend.ForUser(analysis.UserId);
            var settings = await kvas.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
            if (settings is not { IsCoachingEnabled: true, AreLiveTipsEnabled: true })
                return;

            var after = await GetDay(analysis.UserId, record.Day, cancellationToken).ConfigureAwait(false);
            var tipAccessor = kvas.UserCoachTip();
            var previous = await tipAccessor.Get(cancellationToken).ConfigureAwait(false);
            var tip = CoachTipPolicy.Evaluate(
                record,
                before,
                after,
                analysis.Spans,
                previous,
                settings,
                Settings.Coach,
                Clocks.SystemClock.Now,
                analysis.Language?.Value);
            if (tip is not null)
                await tipAccessor.Set(tip with { Origin = previous.Origin }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Tip evaluation failed for {UserId}", analysis.UserId);
        }
    }

    private static bool IsTrackedUser(UserId userId)
        => userId is { IsGuest: false } && !userId.Value.IsNullOrEmpty();
}
