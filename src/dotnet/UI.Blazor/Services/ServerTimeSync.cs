
namespace ActualChat.UI.Blazor.Services;

public class ServerTimeSync : WorkerBase
{
    private const int BurstSize = 4;
    private const double RttToleranceFactor = 1.5;
    private const int MaxRejectStreak = 3;
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(11);
    private static readonly TimeSpan SteadyStateDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DriftCheckPeriod = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SuspendDriftThreshold = TimeSpan.FromSeconds(1);

    private readonly RunningEma _rttEma = new(0f, 1, 0.3);
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private int _rejectStreak;
    private Moment _lastUpdatedWallAt;

    private ILogger Log { get; }
    private MomentClockSet Clocks { get; }
    private MomentClock CpuClock => Clocks.CpuClock;
    private HostInfo HostInfo { get; }
    private ISystemProperties SystemProperties { get; set; }
    private IJSRuntime JS { get; }
    private ServerClockSyncStats Stats { get; }
    public TimeSpan LastOffset { get; private set; }
    public TimeSpan LastPrecision { get; private set; } = TimeSpan.FromHours(1);
    public TimeSpan RttEma => TimeSpan.FromSeconds(_rttEma.Value);
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
        Stats = services.GetRequiredService<ServerClockSyncStats>();
        LastUpdatedAt = Clocks.CpuClock.Now - TimeSpan.FromDays(1);
        _lastUpdatedWallAt = Clocks.SystemClock.Now - TimeSpan.FromDays(1);
    }

    public async Task EnsureSynced(CancellationToken cancellationToken)
    {
        // Forces a sync on demand; call before using server timestamps (e.g. video
        // startedAtMs). Re-syncs after a device suspend too — the offset measured
        // before the suspend is off by exactly the slept time.
        if (SyncCount > 0 && GetSuspendDrift() < SuspendDriftThreshold)
            return;

        await Sync(cancellationToken).ConfigureAwait(false);
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(Sync)
            .RetryForever(RetryDelaySeq.Exp(0.5, 60))
            .AppendDelay(GetNextSyncDelay)
            .CycleForever()
            .Log(LogLevel.Debug, Log)
            .PrependDelay(TimeSpan.FromSeconds(3))
            .RunIsolated(cancellationToken);

    private TimeSpan GetNextSyncDelay()
    {
        if (SyncAttemptCount <= 2)
            return TimeSpan.FromSeconds(0.25);
        if (SyncCount <= 2 && SyncAttemptCount <= 5)
            return TimeSpan.FromSeconds(0.5);

        // Short cadence, but most ticks are cheap no-ops: SyncUnsafe runs a real
        // burst only when SteadyStateDelay elapsed or a device suspend is detected.
        return DriftCheckPeriod;
    }

    private async Task Sync(CancellationToken cancellationToken)
    {
        // Serialize the on-demand EnsureSynced path against the background loop.
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await SyncUnsafe(cancellationToken).ConfigureAwait(false);
        }
        finally {
            _syncLock.Release();
        }
    }

    private async Task SyncUnsafe(CancellationToken cancellationToken)
    {
        var isSuspendSuspected = GetSuspendDrift() >= SuspendDriftThreshold;
        var isDue = CpuClock.Now - LastUpdatedAt >= SteadyStateDelay;
        if (SyncCount > 2 && !isDue && !isSuspendSuspected)
            return; // Drift-check tick, nothing to do

        SyncAttemptCount++;

        // The remote being measured differs by host kind. Server mode: C# runs on
        // the server and the browser is across the SignalR circuit, so we NTP-probe
        // the browser's Date.now() over the interop channel — this compensates the
        // circuit latency (gap the old serverNow one-way push left uncorrected).
        // WASM/MAUI: C# runs in the browser, so we NTP-probe the remote server via
        // RPC. Either way `before`/`after` are the LOCAL clock around the round trip.
        var isServer = HostInfo.HostKind.IsServer();

        // RTT is the wall-clock (CpuClock) span of the whole burst, not a per-call
        // Stopwatch delta: in the browser the Stopwatch/performance.now granularity
        // makes a single fast sample read ~0, and min-RTT then latches onto it,
        // zeroing precision. The burst window over real network awaits can't be 0.
        var burstStart = CpuClock.Now;
        var offsets = new List<double>(BurstSize); // server − client, in ms
        for (var i = 0; i < BurstSize; i++) {
            var before = CpuClock.Now.EpochOffset.TotalMilliseconds;
            double remoteMs;
            try {
                // ProbeTimeout keeps a probe hung on a half-dead connection from
                // wedging this worker forever (it holds _syncLock while awaiting).
                remoteMs = isServer
                    ? await JS.InvokeAsync<double>("SharedSettings.getClientTimeMs", cancellationToken)
                        .AsTask().WaitAsync(ProbeTimeout, cancellationToken).ConfigureAwait(false)
                    : await SystemProperties.GetTime(cancellationToken)
                        .WaitAsync(ProbeTimeout, cancellationToken).ConfigureAwait(false) * 1000;
            }
            catch (TimeoutException) {
                continue; // Hung probe — skip the sample
            }
            catch (Exception e) when (isServer && e is JSDisconnectedException or ObjectDisposedException) {
                return; // Blazor circuit is gone
            }
            var after = CpuClock.Now.EpochOffset.TotalMilliseconds;
            if (after - before > 2000) // This call took too long
                continue;
            var localMid = 0.5 * (before + after);
            offsets.Add(isServer ? localMid - remoteMs : remoteMs - localMid);
        }
        if (offsets.Count == 0)
            return; // every call took too long

        var avgRtt = (CpuClock.Now - burstStart).TotalSeconds / offsets.Count;
        var offsetMs = Median(offsets);
        var offset = TimeSpan.FromMilliseconds(offsetMs);
        var precision = TimeSpan.FromSeconds(0.5 * avgRtt);

        // Tolerance gate against the EMA baseline captured BEFORE folding in this
        // sample: rejects a momentary RTT spike, yet a sustained increase raises
        // the baseline over the next cycles so we never lock out forever.
        var baseline = _rttEma.Value;
        _rttEma.AppendSample(avgRtt);
        var isStale = CpuClock.Now - LastUpdatedAt > StaleThreshold;
        var accept = SyncCount == 0
            || isStale
            || isSuspendSuspected
            || _rejectStreak >= MaxRejectStreak
            || avgRtt <= baseline * RttToleranceFactor;
        if (!accept) {
            _rejectStreak++;
            return;
        }
        _rejectStreak = 0;

        SyncCount++;
        LastOffset = offset;
        LastPrecision = precision;
        LastUpdatedAt = CpuClock.Now;
        _lastUpdatedWallAt = Clocks.SystemClock.Now;
        Log.LogInformation("Offset = {Offset} ± {Precision} (rtt ema {RttEma})",
            offset.ToShortString(), precision.ToShortString(), RttEma.ToShortString());

        if (!isServer) {
            // C# runs in the browser here, so its own ServerClock needs the offset too.
            // In server mode the offset is the browser's, not the server's — leave the
            // server-side ServerClock at zero and only push the value to the browser.
            var serverClock = Clocks.ServerClock;
            serverClock.Offset = offset;
            if (MomentClockSet.Default.ServerClock != serverClock)
                MomentClockSet.Default.ServerClock.Offset = offset;
        }

        Stats.Set(new ServerClockSyncStats.Snapshot(
            LastOffset, LastPrecision, RttEma, TimeSpan.FromSeconds(avgRtt), LastUpdatedAt, SyncCount));

        try {
            // Always a Date-relative offset, never an absolute server timestamp: an
            // absolute value is converted against Date.now() at APPLY time, so any
            // delay between this call and its JS-side execution (doze, frozen
            // WebView) would skew the JS clock by exactly that delay.
            var jsOffsetMs = isServer
                ? offsetMs
                : (Clocks.ServerClock.Now - Clocks.SystemClock.Now).TotalMilliseconds;
            await JS.InvokeVoidAsync("SharedSettings.setServerClockOffsetMs", cancellationToken, jsOffsetMs)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is JSDisconnectedException or ObjectDisposedException) {
            // Blazor circuit is gone — safe to ignore
        }
    }

    private TimeSpan GetSuspendDrift()
    {
        // The wall clock keeps running through a device suspend while CpuClock
        // (Stopwatch / CLOCK_MONOTONIC) does not, so the gap between the two
        // elapsed spans since the last accepted sync equals the time slept since
        // then — which is exactly the error ServerClock has accumulated.
        var wallElapsed = Clocks.SystemClock.Now - _lastUpdatedWallAt;
        var cpuElapsed = CpuClock.Now - LastUpdatedAt;
        return wallElapsed - cpuElapsed;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var n = values.Count;
        return (n & 1) == 1
            ? values[n / 2]
            : 0.5 * (values[(n / 2) - 1] + values[n / 2]);
    }
}
