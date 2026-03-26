using ActualChat.Users.Db;
using ActualLab.Diagnostics;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Internal;

public sealed class DbSessionInfoTrimmer(DbSessionInfoTrimmer.Options settings, IServiceProvider services)
    : DbShardWorkerBase<UsersDbContext>(services)
{
    public sealed record Options
    {
        public int BatchSize { get; init; } = 4096;
        public TimeSpan MaxSessionAge { get; init; } = TimeSpan.FromDays(60);
        public RandomTimeSpan CheckPeriod { get; init; } = TimeSpan.FromMinutes(15).ToRandom(0.25);
        public RetryDelaySeq RetryDelays { get; init; } = RetryDelaySeq.Exp(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(10));
        public LogLevel LogLevel { get; init; } = LogLevel.Information;
    }

    private Options Settings { get; } = settings;
    private MomentClock SystemClock => Clocks.SystemClock;
    private ILogger? DefaultLog => Log.IfEnabled(Settings.LogLevel);

    protected override Task OnRun(string shard, CancellationToken cancellationToken)
        => new AsyncChain($"Trim({shard})", ct => Trim(shard, ct))
            .RetryForever(Settings.RetryDelays, SystemClock, Log)
            .CycleForever()
            .Log(Log)
            .PrependDelay(Settings.CheckPeriod.Next().MultiplyBy(0.1), SystemClock)
            .Start(cancellationToken);

    private async Task Trim(string shard, CancellationToken cancellationToken)
    {
        var batchSize = Settings.BatchSize;

        // Trim by LastSeenAt (old sessions)
        while (true) {
            var maxLastSeenAt = (SystemClock.Now - Settings.MaxSessionAge).ToDateTime();
            try {
                var count = await TrimByLastSeenAt(shard, maxLastSeenAt, batchSize, cancellationToken).ConfigureAwait(false);
                if (count > 0)
                    DefaultLog?.Log(Settings.LogLevel, "Trim({Shard}) trimmed {Count} old sessions", shard, count);
                if (count < batchSize)
                    break;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                Log.LogError(e, "Trim({Shard}): error trimming old sessions", shard);
                throw;
            }
        }

        // Trim by ExpiresAt (expired sessions)
        while (true) {
            var now = SystemClock.Now.ToDateTime();
            try {
                var count = await TrimByExpiresAt(shard, now, batchSize, cancellationToken).ConfigureAwait(false);
                if (count > 0)
                    DefaultLog?.Log(Settings.LogLevel, "Trim({Shard}) trimmed {Count} expired sessions", shard, count);
                if (count < batchSize)
                    break;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                Log.LogError(e, "Trim({Shard}): error trimming expired sessions", shard);
                throw;
            }
        }

        await SystemClock.Delay(Settings.CheckPeriod.Next(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> TrimByLastSeenAt(string shard, DateTime maxLastSeenAt, int maxCount, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(shard, true, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(false);

        // Find sessions to trim and clean up user_sessions entries
        var sessionIds = await dbContext.Sessions
            .Where(o => o.LastSeenAt < maxLastSeenAt)
            .OrderBy(o => o.LastSeenAt)
            .Take(maxCount)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sessionIds.Count == 0)
            return 0;

        // Remove corresponding user_sessions entries
        await dbContext.UserSessions
            .Where(us => sessionIds.Contains(us.SessionId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // Delete the sessions
        return await dbContext.Sessions
            .Where(o => sessionIds.Contains(o.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<int> TrimByExpiresAt(string shard, DateTime now, int maxCount, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(shard, true, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(false);

        // Find expired sessions
        var sessionIds = await dbContext.Sessions
            .Where(o => o.ExpiresAt != null && o.ExpiresAt < now)
            .OrderBy(o => o.ExpiresAt)
            .Take(maxCount)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sessionIds.Count == 0)
            return 0;

        // Remove corresponding user_sessions entries
        await dbContext.UserSessions
            .Where(us => sessionIds.Contains(us.SessionId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // Delete the sessions
        return await dbContext.Sessions
            .Where(o => sessionIds.Contains(o.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
