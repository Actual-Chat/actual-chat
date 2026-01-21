using ActualChat.Users.Db;
using ActualLab.Diagnostics;
using ActualLab.Fusion.EntityFramework;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Users.Services;

public class DbSessionInfoTrimmer(
    DbSessionInfoTrimmer.Options settings,
    IServiceProvider services)
    : BackgroundService
{
    public record Options
    {
        public int BatchSize { get; init; } = 4096;
        public TimeSpan MaxSessionAge { get; init; } = TimeSpan.FromDays(60);
        public RandomTimeSpan CheckPeriod { get; init; } = TimeSpan.FromMinutes(15).ToRandom(0.25);
        public RetryDelaySeq RetryDelays { get; init; } = RetryDelaySeq.Exp(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(10));
        public LogLevel LogLevel { get; init; } = LogLevel.Information;
    }

    protected Options Settings { get; } = settings;
    protected IDbSessionInfoRepo<DbSessionInfo, string> Sessions { get; }
        = services.GetRequiredService<IDbSessionInfoRepo<DbSessionInfo, string>>();
    protected MomentClock SystemClock => services.Clocks().SystemClock;
    protected ILogger Log { get; } = services.LogFor<DbSessionInfoTrimmer>();
    protected ILogger? DefaultLog => Log.IfEnabled(Settings.LogLevel);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => new AsyncChain("Trim", ct => Trim(ct))
            .RetryForever(Settings.RetryDelays, SystemClock, Log)
            .CycleForever()
            .Log(Log)
            .PrependDelay(Settings.CheckPeriod.Next().MultiplyBy(0.1), SystemClock)
            .Start(stoppingToken);

    protected virtual async Task Trim(CancellationToken cancellationToken)
    {
        var batchSize = Settings.BatchSize;
        while (true) {
            var maxLastSeenAt = (SystemClock.Now - Settings.MaxSessionAge).ToDateTime();
            try {
                var count = await Sessions.Trim(maxLastSeenAt, batchSize, cancellationToken)
                    .ConfigureAwait(false);
                if (count > 0)
                    DefaultLog?.Log(Settings.LogLevel, "Trim() trimmed {Count} sessions", count);
                if (count < batchSize)
                    break;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                Log.LogError(e, "Error trimming sessions");
                throw;
            }
        }
        await SystemClock.Delay(Settings.CheckPeriod.Next(), cancellationToken).ConfigureAwait(false);
    }
}
