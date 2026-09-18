using ActualChat.Chat.Db;
using ActualChat.WebHooks;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

/// <summary>
/// Hourly sweep of the delivery log: rows past <see cref="Constants.WebHooks.DeliveryRetention"/>
/// are removed, but a <see cref="WebHookDeliveryStatus.Pending"/> row is never touched.
/// </summary>
public sealed class WebHookDeliveryPruner : WorkerBase
{
    private static readonly RandomTimeSpan Period = TimeSpan.FromHours(1).ToRandom(0.25);
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(5);
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(30, 600);

    private DbHub<ChatDbContext> DbHub { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public WebHookDeliveryPruner(IServiceProvider services)
    {
        DbHub = services.DbHub<ChatDbContext>();
        Clocks = services.Clocks();
        Log = services.LogFor(GetType());
    }

    public async Task RunOnce(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var cutoff = (Clocks.SystemClock.Now - Constants.WebHooks.DeliveryRetention).ToDateTime();
        var prunedCount = await dbContext.WebHookDeliveries
            .Where(x => x.Status != WebHookDeliveryStatus.Pending && x.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (prunedCount > 0)
            Log.LogInformation("Pruned {Count} expired web hook deliveries", prunedCount);
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
