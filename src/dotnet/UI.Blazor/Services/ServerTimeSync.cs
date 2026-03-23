using ActualChat.Hosting;
using ActualChat.Users;
using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor.Services;

public class ServerTimeSync : WorkerBase
{
    private ILogger Log { get; }
    private MomentClockSet Clocks { get; }
    private MomentClock CpuClock => Clocks.CpuClock;
    private HostInfo HostInfo { get; }
    private ISystemProperties SystemProperties { get; set; }
    private IJSRuntime JS { get; }
    private BackgroundStateTracker BackgroundStateTracker { get; }

    public TimeSpan LastOffset { get; private set; }
    public TimeSpan LastPrecision { get; private set; } = TimeSpan.FromHours(1);
    public Moment LastUpdatedAt { get; private set; }
    public int SyncAttemptCount { get; private set; }
    public int SyncCount { get; private set; }
    // Precision "auto-grows" by 1 second per day
    public TimeSpan Precision => LastPrecision + TimeSpan.FromSeconds((CpuClock.Now - LastUpdatedAt).TotalDays);

    public ServerTimeSync(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        HostInfo = services.HostInfo();
        SystemProperties = services.GetRequiredService<ISystemProperties>();
        JS = services.JSRuntime();
        BackgroundStateTracker = services.GetRequiredService<BackgroundStateTracker>();
        LastUpdatedAt = Clocks.CpuClock.Now - TimeSpan.FromDays(1);
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        if (!HostInfo.HostKind.IsApp()) {
            Log.LogInformation("Exit: not a client");
            return Task.CompletedTask;
        }

        var retryDelays = RetryDelaySeq.Exp(0.5, 60);
        var baseChains = new[] {
            AsyncChain.From(Sync)
                .RetryForever(retryDelays)
                .AppendDelay(GetNextSyncDelay)
                .CycleForever()
                .PrependDelay(TimeSpan.FromSeconds(3)),
            AsyncChain.From(WatchForegroundTransitions)
                .RetryForever(retryDelays),
        };
        return baseChains
            .Select(chain => chain.Log(LogLevel.Debug, Log))
            .RunIsolated(cancellationToken);
    }

    private async Task WatchForegroundTransitions(CancellationToken cancellationToken)
    {
        var wasBackground = false;
        var changes = BackgroundStateTracker.IsBackground.Computed.Changes(cancellationToken);
        await foreach (var (isBackground, _) in changes.ConfigureAwait(false)) {
            if (wasBackground && !isBackground) {
                Log.LogInformation("Returned to foreground, forcing clock re-sync");
                LastPrecision = TimeSpan.FromHours(1);
                try {
                    await Sync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) {
                    Log.LogWarning(e, "Foreground re-sync failed");
                }
            }
            wasBackground = isBackground;
        }
    }

    private TimeSpan GetNextSyncDelay()
    {
        if (SyncAttemptCount <= 2)
            return TimeSpan.FromSeconds(0.25);
        if (SyncCount <= 2 && SyncAttemptCount <= 5)
            return TimeSpan.FromSeconds(0.5);

        return Precision > TimeSpan.FromSeconds(0.5)
            ? TimeSpan.FromMinutes(5)
            : TimeSpan.FromHours(1);
    }

    private async Task Sync(CancellationToken cancellationToken)
    {
        var now = CpuClock.Now.EpochOffset.TotalSeconds;
        var startedAt = CpuTimestamp.Now;
        var serverNow = await SystemProperties.GetTime(cancellationToken).ConfigureAwait(false);
        var callDuration = startedAt.Elapsed.TotalSeconds;

        SyncAttemptCount++;
        if (callDuration > 2) // This took too long
            return;

        var adjustedNow = now + (0.5 * callDuration);
        var offset = TimeSpan.FromSeconds(serverNow - adjustedNow);
        var precision = TimeSpan.FromSeconds(0.5 * callDuration);
        if (precision >= Precision)
            return;

        SyncCount++;
        LastOffset = offset;
        LastPrecision = precision;
        LastUpdatedAt = CpuClock.Now;
        Log.LogInformation("Offset = {Offset} ± {Precision}", offset.ToShortString(), precision.ToShortString());

        var serverClock = Clocks.ServerClock;
        serverClock.Offset = offset;
        if (MomentClockSet.Default.ServerClock != serverClock)
            MomentClockSet.Default.ServerClock.Offset = offset;

        try {
            var serverNowMs = Clocks.ServerClock.Now.EpochOffset.TotalMilliseconds;
            await JS.InvokeVoidAsync("ServerClock.updateOffset", cancellationToken, serverNowMs)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is JSDisconnectedException or ObjectDisposedException) {
            // Blazor circuit is gone — safe to ignore
        }
    }
}
