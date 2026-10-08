using ActualChat.Db;
using ActualChat.Kvas;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

public partial class CoachBackend
{
    public virtual async Task<CoachBaseline?> GetBaseline(
        UserId userId, CoachMetricKind kind, string language, CancellationToken cancellationToken)
    {
        await ListAllDays(userId, cancellationToken).ConfigureAwait(false);
        var key = BaselineKey(userId, kind, language);
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var row = await dbContext.KvasEntries.SingleOrDefaultAsync(e => e.Key == key, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ReadBaseline(row);
    }

    public virtual async Task<CoachBaselineComparison> GetBaselineComparison(
        UserId userId, CoachMetricKind kind, string language, Range<Moment> range,
        CancellationToken cancellationToken)
    {
        var baseline = await GetBaseline(userId, kind, language, cancellationToken).ConfigureAwait(false);
        if (baseline is null)
            return new CoachBaselineComparison();

        var now = Clocks.SystemClock.Now;
        if (baseline.InvalidatedAt is not null || range.End <= baseline.CapturedAt)
            return CoachBaselineBuilder.Compare(baseline, range, now, [], Settings.Coach);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var start = range.Start;
        var records = await ListBaselineRecords(dbContext, userId,
            new Range<Moment>(start, Moment.Min(range.End, now)), cancellationToken).ConfigureAwait(false);
        return CoachBaselineBuilder.Compare(baseline, range, now, records, Settings.Coach);
    }

    public virtual async Task OnSetBaseline(CoachBackend_SetBaseline command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        var language = Language.GetIsoCode(command.Language);
        var key = BaselineKey(userId, command.Kind, language);
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        await dbContext.CoachDays.Lock(userId.Value, cancellationToken).ConfigureAwait(false);
        var row = await dbContext.KvasEntries.SingleOrDefaultAsync(e => e.Key == key, cancellationToken)
            .ConfigureAwait(false);
        if (command.Period is { } period) {
            var now = Clocks.SystemClock.Now;
            var range = CoachHistoryRanges.Get(period, command.Anchor, now);
            var records = await ListBaselineRecords(dbContext, userId, range, cancellationToken).ConfigureAwait(false);
            var baseline = CoachBaselineBuilder.Capture(range, now, command.Kind, language, records, Settings.Coach);
            if (row is null) {
                row = new DbKvasEntry { Key = key };
                dbContext.KvasEntries.Add(row);
            }
            row.Value = SerializeBaseline(baseline);
            row.Version = VersionGenerator.NextVersion(row.Version);
        }
        else if (row is not null)
            dbContext.KvasEntries.Remove(row);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => {
            _ = GetBaseline(userId, command.Kind, language, default);
            _ = ListAllDays(userId, default);
        });
    }

    private async Task<List<CoachRecord>> ListBaselineRecords(
        UsersDbContext dbContext, UserId userId, Range<Moment> range, CancellationToken cancellationToken)
    {
        var start = range.Start.ToDateTimeClamped();
        var end = range.End.ToDateTimeClamped();
        var rows = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Kind == CoachRecordKind.Entry && !e.IsRemoved && !e.IsExcluded)
            .Where(e => e.OccurredAt >= start && e.OccurredAt < end)
            .Take(CoachBaselineBuilder.MaxSources + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count > CoachBaselineBuilder.MaxSources)
            throw new InvalidOperationException("Too many baseline sources; choose a shorter period.");

        return rows.Select(r => r.ToModel()).ToList();
    }

    private async Task InvalidateBaselines(
        UsersDbContext dbContext, UserId userId, IEnumerable<string> sourceIds, CancellationToken cancellationToken)
    {
        var prefix = BaselinePrefix(userId);
        var sources = sourceIds.ToHashSet();
        var rows = await dbContext.KvasEntries.Where(e => e.Key.StartsWith(prefix))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) {
            var baseline = ReadBaseline(row);
            if (baseline is null || baseline.InvalidatedAt is not null
                || !baseline.Sources.Any(s => sources.Contains(s.SourceId)))
                continue;

            row.Value = SerializeBaseline(baseline with { InvalidatedAt = Clocks.SystemClock.Now });
            row.Version = VersionGenerator.NextVersion(row.Version);
        }
    }

    private static string BaselineKey(UserId userId, CoachMetricKind kind, string language)
    {
        if (kind is not (CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Pace)
            || language.IsNullOrWhiteSpace())
            throw new ArgumentOutOfRangeException(nameof(kind));

        return BaselinePrefix(userId) + Language.GetIsoCode(language) + "/" + (int)kind;
    }

    private static string BaselinePrefix(UserId userId)
        => UserScopedKvasBackend.GetUserPrefix(userId) + "CoachBaselines/";

    private static CoachBaseline? ReadBaseline(DbKvasEntry row)
        => (CoachBaseline?)KvasSerializer.Default.Read(row.Value, typeof(CoachBaseline), out _);

    private static byte[] SerializeBaseline(CoachBaseline baseline)
    {
        using var buffer = KvasSerializer.Default.Write(baseline);
        return buffer.WrittenMemory.ToArray();
    }
}
