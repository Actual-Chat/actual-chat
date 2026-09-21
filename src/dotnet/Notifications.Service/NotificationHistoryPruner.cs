using ActualChat.Notifications.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Notifications;

/// <summary>
/// Hourly sweep deleting notification history rows older than
/// <see cref="Constants.Notification.HistoryRetention"/>.
/// </summary>
public sealed class NotificationHistoryPruner : WorkerBase
{
    private static readonly RandomTimeSpan Period = TimeSpan.FromHours(1).ToRandom(0.25);
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(5);
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(30, 600);

    private DbHub<NotificationDbContext> DbHub { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public NotificationHistoryPruner(IServiceProvider services)
    {
        DbHub = services.DbHub<NotificationDbContext>();
        Clocks = services.Clocks();
        Log = services.LogFor(GetType());
    }

    public async Task RunOnce(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var cutoff = (Clocks.SystemClock.Now - Constants.Notification.HistoryRetention).ToDateTime();
        var prunedCount = await dbContext.NotificationHistory
            .Where(x => x.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (prunedCount > 0)
            Log.LogInformation("Pruned {Count} expired notification history rows", prunedCount);
    }

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
        // PrependDelay wraps the cycling chain, so it staggers the host once rather than every cycle
        => AsyncChain.From(RunOnce)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelays, Log)
            .AppendDelay(Period, Clocks.CpuClock)
            .CycleForever()
            .PrependDelay(FirstDelay, Clocks.CpuClock)
            .Run(cancellationToken);
}
