using ActualLab.Diagnostics;
using ActualLab.Fusion.Diagnostics;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.LogProcessing;
using ActualLab.Fusion.EntityFramework.Operations;
using ActualLab.Fusion.EntityFramework.Operations.LogProcessing;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Db.Module;

public class FilteredDbEventLogReader<TDbContext>(
    DbEventLogReader<TDbContext>.Options settings,
    IServiceProvider services)
    : DbEventLogReader<TDbContext>(settings, services)
    where TDbContext : DbContext
{
    protected override async Task<Moment> ProcessBatch(string shard, int batchSize, CancellationToken cancellationToken)
    {
        var activity = FusionInstruments.ActivitySource
            .IfEnabled(Settings.IsTracingEnabled)
            .StartActivity(GetType())
            .AddShardTags(shard);
        try {
            var dbContext = await DbHub.CreateDbContext(shard, readWrite: true, cancellationToken)
                .ConfigureAwait(false);
            await using var _1 = dbContext.ConfigureAwait(false);
            var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var _2 = tx.ConfigureAwait(false);
            dbContext.EnableChangeTracking(false);

            var now = SystemClock.Now.ToDateTime();
            var dbEntries = dbContext.Set<DbEvent>();
            var entries = await dbEntries.WithHints(LogKind.GetReadBatchQueryHints())
                // We override this method to utilize filtered index ix_events_pending
                .Where(o => o.State == LogEntryState.New && o.DelayUntil < now)
                .OrderBy(o => o.DelayUntil)
                .Take(batchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (entries.Count == 0)
                return await GetMinDelayUntil(dbEntries, cancellationToken).ConfigureAwait(false);

            var logLevel = entries.Count == batchSize ? LogLevel.Warning : LogLevel.Debug;
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.IfEnabled(logLevel)?.Log(logLevel,
                $"{nameof(ProcessBatch)}[{{Shard}}]: got {{Count}}/{{BatchSize}} entries",
                shard, entries.Count, batchSize);

            var results = await GetProcessTasks(shard, entries, cancellationToken)
                .Collect(Settings.ConcurrencyLevel, useCurrentScheduler: false, cancellationToken)
                .ConfigureAwait(false);

            var entriesZipped = entries.Zip(results, static (entry, isProcessed) => (entry, isProcessed));
            foreach (var (entry, isProcessed) in entriesZipped) {
                if (isProcessed)
                    SetEntryState(dbEntries, entry, LogEntryState.Processed);
            }
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (entries.Count >= batchSize)
                return default; // Full batch = there might be more entries

            // Partial batch - check if there are upcoming delayed entries
            return await GetMinDelayUntil(dbEntries, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            activity?.Finalize(e, cancellationToken);
            throw;
        }
        finally {
            activity?.Dispose();
        }
    }

    private static new async Task<Moment> GetMinDelayUntil(
        DbSet<DbEvent> dbEntries, CancellationToken cancellationToken)
    {
        var minDelayUntil = await dbEntries
            .Where(o => o.State == LogEntryState.New)
            .MinAsync(o => (DateTime?)o.DelayUntil, cancellationToken)
            .ConfigureAwait(false);
        return minDelayUntil.DefaultKind(DateTimeKind.Utc).ToMoment() ?? Moment.MaxValue;
    }
}
