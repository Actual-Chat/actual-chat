using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Maintains <see cref="ServerClock"/>'s offset: targets a fixed precision and
/// derives its cadence from it, rather than syncing on a fixed schedule.
/// See <c>docs/architecture/server-clock-sync.md</c>.
/// </summary>
public sealed class ServerTimeSync(IServiceProvider services) : WorkerBase
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TargetPrecision = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PrecisionCeiling = TimeSpan.FromMilliseconds(250);
    // Oscillator drift is unmeasurable at this cadence and precision, so it's assumed.
    // 50 ppm covers commodity hardware with a temperature swing; being conservative here
    // only syncs more often than necessary.
    private const double AssumedDriftRate = 50e-6;
    private static readonly TimeSpan DriftBudget = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan SteadyInterval =
        TimeSpan.FromSeconds(DriftBudget.TotalSeconds / AssumedDriftRate)
            .Clamp(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
    // An undisciplined machine can drift 40x the assumption (2000 ppm measured on a dev box),
    // so the cadence follows the measured rate when corrections exceed measurement noise.
    // At this floor even 2000 ppm accrues only ~40 ms per interval - still a slewed refinement.
    private static readonly TimeSpan MinSteadyInterval = TimeSpan.FromSeconds(20);
    private const double DriftRateDecay = 0.7;
    private static readonly RetryDelaySeq ConvergingDelays = RetryDelaySeq.Exp(1, 60);
    private const int ConvergingBurstSize = 8;
    private const int SteadyBurstSize = 4;
    private const int MinUsableProbes = 3;
    private static readonly TimeSpan ProbeSpacing = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(400);
    // A real round trip can't be sub-millisecond: below this the timer resolved nothing,
    // which must read as "unmeasurable" rather than "excellent" - otherwise min-RTT
    // latches onto a granularity artifact and reports zero precision.
    private static readonly TimeSpan PhysicalRttFloor = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan TimerGranularity = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan MaxProbeRtt = TimeSpan.FromSeconds(2);
    private const double RttSelectionFactor = 1.5;
    private const double SmallCorrectionFactor = 2;
    private const double ConfirmFactor = 2;
    private static readonly TimeSpan ConfirmSpacing = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SlewStep = TimeSpan.FromMilliseconds(250);
    // Must stay below 100%, or a negative correction makes ServerClock.Now run backwards.
    private const double MaxSlewRate = 0.1;
    private static readonly TimeSpan StalenessRelaxTime = TimeSpan.FromMinutes(30);
    // Applied to the 0.5*minRtt precision floor so mode doesn't flap when bursts land exactly on it.
    private const double LinkFloorHeadroom = 1.2;
    private static readonly TimeSpan SuspendDriftThreshold = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ConnectWait = TimeSpan.FromSeconds(5);
    // Multiplies the steady interval to bound how old an accepted sync may be before
    // EnsureSynced stops trusting it. Its inputs (precision, drift) are frozen at the last
    // accept, so without an age term the gate stays satisfied however long the loop fails.
    private const int StaleSyncFactor = 2;
    // Probe RTT beyond this multiple of the measured min-RTT is a timeout, not a slow link.
    private const int ProbeTimeoutRttFactor = 3;
    private const int FailureLogPeriod = 5;

    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly RunningEma _minRttEma = new(0f, 1, 0.3);
    private CancellationTokenSource? _slewTokenSource;
    private TimeSpan _appliedOffset;
    private TimeSpan _precision = PrecisionCeiling;
    private TimeSpan _connectedElapsed;
    private Moment _nextSyncAt;
    private Moment _lastAttemptAt;
    private Moment _lastAcceptedAt;
    private Moment _lastAcceptedWallAt;
    private ServerClockSyncMode _mode = ServerClockSyncMode.Converging;
    private double _driftRate;
    private int _attempt;
    private int _epoch;

    private IServiceProvider Services { get; } = services;
    private MomentClockSet Clocks { get; } = services.Clocks();
    private MomentClock CpuClock => Clocks.CpuClock;
    private bool IsServer { get; } = services.HostInfo().HostKind.IsServer();
    private ISystemProperties SystemProperties { get; } = services.GetRequiredService<ISystemProperties>();
    private IJSRuntime JS { get; } = services.JSRuntime();
    private ServerClockSyncStats Stats { get; } = services.GetRequiredService<ServerClockSyncStats>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public int SyncCount { get; private set; }
    public TimeSpan? MinRtt
        // Null until a burst lands, so an unmeasured link never reads as a fast one.
        => _minRttEma.SampleCount > 0 ? TimeSpan.FromSeconds(_minRttEma.Value) : null;

    public async Task EnsureSynced(CancellationToken cancellationToken)
    {
        // Wall-vs-monotonic divergence is a reason to re-measure, never evidence that
        // a correction is legitimate - the two clocks disagree on sleep and on a wall
        // step alike, and which one lies is platform-dependent.
        if (SyncCount > 0
            && _precision <= GetEffectiveTargetPrecision()
            && GetPredictedDrift() <= TargetPrecision
            && GetStaleness() <= StaleSyncFactor * GetSteadyInterval()
            && GetSuspendDrift() < SuspendDriftThreshold)
            return;

        await Sync(cancellationToken).ConfigureAwait(false);
    }

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(Sync)
            .RetryForever(ConvergingDelays)
            .AppendDelay(GetNextSyncDelay)
            .CycleForever()
            .Log(LogLevel.Debug, Log)
            .PrependDelay(StartupDelay)
            .RunIsolated(cancellationToken);

    // Private methods

    private TimeSpan GetNextSyncDelay()
        => (_nextSyncAt - CpuClock.Now).Positive();

    private async Task Sync(CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await SyncUnsafe(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            // A failed sync must not surface to EnsureSynced's callers - a recording
            // start shouldn't fail because the clock probe did.
            Log.LogWarning(e, "Sync failed");
            OnAttemptFailed(ServerClockSyncOutcome.BurstDiscarded);
        }
        finally {
            _syncLock.Release();
        }
    }

    private async Task SyncUnsafe(CancellationToken cancellationToken)
    {
        AccrueStaleness();
        var burst = await RunBurst(cancellationToken).ConfigureAwait(false);
        if (burst == null) {
            OnAttemptFailed(ServerClockSyncOutcome.BurstDiscarded);
            return;
        }

        if (burst.Precision > GetAcceptablePrecision()) {
            OnAttemptFailed(ServerClockSyncOutcome.RejectNoisy);
            return;
        }

        if (SyncCount == 0) {
            Accept(burst, ServerClockSyncOutcome.Step);
            return;
        }

        var correction = (burst.Offset - _appliedOffset).Duration();
        if (correction <= SmallCorrectionFactor * TimeSpanExt.Max(burst.Precision, _precision)) {
            Accept(burst, ServerClockSyncOutcome.Refinement);
            return;
        }

        // A large correction is either a real clock step or a bad sample, and local
        // clocks can't tell them apart. Repeating the measurement can: a step persists,
        // a bad sample doesn't. This also catches a server-side step or a reroute onto
        // a pod with a different clock, which no local inspection could explain.
        await CpuClock.Delay(ConfirmSpacing, cancellationToken).ConfigureAwait(false);
        var confirmation = await RunBurst(cancellationToken).ConfigureAwait(false);
        if (confirmation == null || confirmation.Precision > GetAcceptablePrecision()) {
            OnAttemptFailed(ServerClockSyncOutcome.BurstDiscarded);
            return;
        }

        var agreement = (confirmation.Offset - burst.Offset).Duration();
        if (agreement > ConfirmFactor * TimeSpanExt.Max(burst.Precision, confirmation.Precision)) {
            OnAttemptFailed(ServerClockSyncOutcome.RejectTransient);
            return;
        }

        Accept(confirmation, ServerClockSyncOutcome.Step);
    }

    private async Task<BurstResult?> RunBurst(CancellationToken cancellationToken)
    {
        var offsetBaseClock = IsServer ? CpuClock : Clocks.ServerClock.BaseClock;
        var peer = IsServer ? null : Services.RpcHub().GetClientPeer(RpcRef.Default);
        RpcPeerConnectionState? connectionState = null;
        if (peer != null) {
            // Probing while disconnected parks the call until reconnect, and a probe that
            // resumes afterwards reports a server reading taken near `after` rather than
            // the midpoint - biasing the offset positive by half the outage.
            try {
                await peer.WhenConnected(ConnectWait, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                return null;
            }

            connectionState = peer.ConnectionState.Value;
        }

        var burstSize = _mode == ServerClockSyncMode.Steady ? SteadyBurstSize : ConvergingBurstSize;
        var samples = new List<Sample>(burstSize);
        for (var i = 0; i < burstSize; i++) {
            if (i != 0)
                await CpuClock.Delay(ProbeSpacing, cancellationToken).ConfigureAwait(false);

            Sample? sample;
            try {
                sample = await Probe(offsetBaseClock, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is JSDisconnectedException or ObjectDisposedException) {
                return null; // The circuit is gone - the whole burst is void
            }

            if (sample is { } usableSample)
                samples.Add(usableSample);
        }

        if (peer != null && !IsSameConnection(connectionState!, peer.ConnectionState.Value))
            return null;
        if (samples.Count < MinUsableProbes)
            return null;

        return Reduce(samples);
    }

    private async Task<Sample?> Probe(MomentClock offsetBaseClock, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(GetProbeTimeout());
        var before = offsetBaseClock.Now;
        double remoteMs;
        try {
            remoteMs = IsServer
                ? await JS.InvokeAsync<double>("SharedSettings.getClientTimeMs", cts.Token).ConfigureAwait(false)
                : await SystemProperties.GetTime(cts.Token).ConfigureAwait(false) * 1000;
        }
        catch (Exception e) when (e is not (JSDisconnectedException or ObjectDisposedException)
                                  && !e.IsCancellationOf(cancellationToken)) {
            return null; // Timed out or failed: this sample is unusable, the burst isn't
        }

        var after = offsetBaseClock.Now;
        var rtt = after - before;
        if (rtt < PhysicalRttFloor || rtt > MaxProbeRtt)
            return null;

        var localMidMs = 0.5 * (before.EpochOffset.TotalMilliseconds + after.EpochOffset.TotalMilliseconds);
        return new Sample(rtt, IsServer ? localMidMs - remoteMs : remoteMs - localMidMs);
    }

    private void Accept(BurstResult burst, ServerClockSyncOutcome outcome)
    {
        var correction = (burst.Offset - _appliedOffset).Duration();
        UpdateDriftRate(correction, burst.Precision);
        SyncCount++;
        _attempt = 0;
        _connectedElapsed = TimeSpan.Zero;
        _precision = burst.Precision;
        _lastAcceptedAt = CpuClock.Now;
        _lastAcceptedWallAt = Clocks.SystemClock.Now;
        _minRttEma.AppendSample((float)burst.MinRtt.TotalSeconds);
        _mode = burst.Precision <= GetEffectiveTargetPrecision()
            ? ServerClockSyncMode.Steady
            : ServerClockSyncMode.Converging;
        if (outcome == ServerClockSyncOutcome.Step)
            ApplyStep(burst.Offset);
        else
            StartSlew(burst.Offset);

        Log.LogInformation(
            "{Outcome}: offset = {Offset} ± {Precision}, correction = {Correction}, minRtt = {MinRtt}, "
            + "drift = {DriftPpm:F0}ppm, epoch = {Epoch}",
            outcome, burst.Offset.ToShortString(), burst.Precision.ToShortString(),
            correction.ToShortString(), burst.MinRtt.ToShortString(), _driftRate * 1e6, _epoch);
        SetNextSyncAt();
        UpdateStats(outcome, burst, correction);
    }

    private void OnAttemptFailed(ServerClockSyncOutcome outcome)
    {
        _attempt++;
        _mode = ServerClockSyncMode.Converging;
        // Rejections were entirely silent, which is why a client stuck for hours looked healthy.
        if (_attempt % FailureLogPeriod == 0)
            Log.LogWarning("{Outcome}: {Attempt} consecutive failures, {SyncCount} accepts so far",
                outcome, _attempt, SyncCount);
        SetNextSyncAt();
        UpdateStats(outcome, null, TimeSpan.Zero);
    }

    private void SetNextSyncAt()
        => _nextSyncAt = CpuClock.Now + (_mode == ServerClockSyncMode.Steady
            ? GetSteadyInterval()
            : ConvergingDelays[_attempt]);

    private TimeSpan GetSteadyInterval()
        => _driftRate <= AssumedDriftRate
            ? SteadyInterval
            : TimeSpan.FromSeconds(DriftBudget.TotalSeconds / _driftRate)
                .Clamp(MinSteadyInterval, SteadyInterval);

    private void UpdateDriftRate(TimeSpan correction, TimeSpan burstPrecision)
    {
        if (_lastAcceptedAt == default)
            return;

        // Below MinSteadyInterval the expected drift sits under the noise floor, so the
        // sample can neither confirm nor refute the estimate - it must not decay it either.
        var elapsed = (CpuClock.Now - _lastAcceptedAt).TotalSeconds;
        if (elapsed < MinSteadyInterval.TotalSeconds)
            return;

        var noise = TimeSpanExt.Max(burstPrecision, _precision);
        var evidence = correction - noise;
        _driftRate = evidence > TimeSpan.Zero
            ? evidence.TotalSeconds / elapsed
            : _driftRate * DriftRateDecay;
    }

    private TimeSpan GetProbeTimeout()
        // A fixed timeout locks out a link that got slower for good: _minRttEma only updates on
        // accept, so once every probe times out nothing is left to widen it.
        => TimeSpanExt.Min(
            MaxProbeRtt,
            TimeSpanExt.Max(ProbeTimeout, TimeSpan.FromSeconds(ProbeTimeoutRttFactor * _minRttEma.Value)));

    private TimeSpan GetStaleness()
        => SyncCount == 0 ? TimeSpan.MaxValue : (CpuClock.Now - _lastAcceptedAt).Positive();

    private TimeSpan GetPredictedDrift()
    {
        if (_lastAcceptedAt == default)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds(_driftRate * (CpuClock.Now - _lastAcceptedAt).TotalSeconds);
    }

    private TimeSpan GetAcceptablePrecision()
    {
        if (SyncCount == 0)
            return PrecisionCeiling;

        var relaxation = 1 + (_connectedElapsed.TotalSeconds / StalenessRelaxTime.TotalSeconds);
        return TimeSpanExt.Min(relaxation * GetEffectiveTargetPrecision(), PrecisionCeiling);
    }

    // A target below the link's 0.5*minRtt floor is unattainable: every burst reads "noisy",
    // the loop rejects sound measurements for the whole relaxation window, and the offset
    // free-runs on exactly the links (and machines) that drift. On such links the floor is
    // the honest target; the fixed one still governs whenever the link can do better.
    private TimeSpan GetEffectiveTargetPrecision()
    {
        var linkFloor = TimeSpan.FromSeconds(0.5 * LinkFloorHeadroom * _minRttEma.Value);
        return TimeSpanExt.Max(TargetPrecision, linkFloor);
    }

    private void AccrueStaleness()
    {
        // Staleness must accrue in connected time only: a client returning from an hour
        // offline would otherwise arrive with a fully relaxed precision bound and swallow
        // the first sample it gets - which is the one most likely to be reconnect-poisoned.
        var now = CpuClock.Now;
        if (_lastAttemptAt != default) {
            var scheduled = _mode == ServerClockSyncMode.Steady ? GetSteadyInterval() : ConvergingDelays[_attempt];
            _connectedElapsed += TimeSpanExt.Min((now - _lastAttemptAt).Positive(), scheduled);
        }

        _lastAttemptAt = now;
    }

    private void ApplyStep(TimeSpan offset)
    {
        _epoch++;
        CancelSlew();
        SetOffset(offset);
    }

    private void StartSlew(TimeSpan target)
    {
        CancelSlew();
        var slewTokenSource = _slewTokenSource = StopToken.CreateLinkedTokenSource();
        _ = BackgroundTask.Run(
            () => Slew(target, slewTokenSource),
            Log, "Server clock slew failed", CancellationToken.None);
    }

    private async Task Slew(TimeSpan target, CancellationTokenSource slewTokenSource)
    {
        var cancellationToken = slewTokenSource.Token;
        var maxStep = MaxSlewRate * SlewStep;
        while (!cancellationToken.IsCancellationRequested) {
            var delta = target - _appliedOffset;
            if (delta.Duration() <= maxStep) {
                SetOffsetFromSlew(target, slewTokenSource);
                return;
            }

            SetOffsetFromSlew(_appliedOffset + (delta > TimeSpan.Zero ? maxStep : -maxStep), slewTokenSource);
            await CpuClock.Delay(SlewStep, cancellationToken).ConfigureAwait(false);
        }
    }

    private void SetOffsetFromSlew(TimeSpan offset, CancellationTokenSource slewTokenSource)
    {
        // Cancellation alone can't stop a write already past its check: prove ownership too.
        if (!ReferenceEquals(_slewTokenSource, slewTokenSource))
            return;

        SetOffset(offset);
    }

    private void CancelSlew()
    {
        _slewTokenSource.CancelAndDisposeSilently();
        _slewTokenSource = null;
    }

    private void SetOffset(TimeSpan offset)
    {
        _appliedOffset = offset;
        if (!IsServer) {
            // In server mode the measured offset is the browser's, so the server-side
            // ServerClock stays at zero and only the pushed value matters.
            var serverClock = Clocks.ServerClock;
            serverClock.Offset = offset;
            if (MomentClockSet.Default.ServerClock != serverClock)
                MomentClockSet.Default.ServerClock.Offset = offset;
        }

        _ = PushToJS();
    }

    private async Task PushToJS()
    {
        try {
            // Always a Date-relative offset, never an absolute server timestamp: an absolute
            // value is converted against Date.now() at APPLY time, so any delay between this
            // call and its JS-side execution would skew the JS clock by exactly that delay.
            var offsetMs = IsServer
                ? _appliedOffset.TotalMilliseconds
                : (Clocks.ServerClock.Now - Clocks.SystemClock.Now).TotalMilliseconds;
            await JS.InvokeVoidAsync("SharedSettings.setServerClockOffsetMs", StopToken, offsetMs, _epoch)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is JSDisconnectedException or ObjectDisposedException) {
            // The circuit is gone - nothing to push to
        }
        catch (Exception e) when (!e.IsCancellationOf(StopToken)) {
            Log.LogWarning(e, "Failed to push the server clock offset to JS");
        }
    }

    private void UpdateStats(ServerClockSyncOutcome outcome, BurstResult? burst, TimeSpan correction)
        => Stats.Set(new ServerClockSyncStats.Snapshot(
            _appliedOffset,
            _precision,
            TimeSpan.FromSeconds(_minRttEma.Value),
            burst?.MinRtt ?? TimeSpan.Zero,
            _lastAcceptedAt,
            _lastAcceptedWallAt,
            SyncCount) {
            Dispersion = burst?.Dispersion ?? TimeSpan.Zero,
            Correction = correction,
            Epoch = _epoch,
            Mode = _mode,
            Staleness = _connectedElapsed,
            Outcome = outcome,
            DriftRate = _driftRate,
        });

    private TimeSpan GetSuspendDrift()
    {
        // The wall clock keeps running through a device suspend while CpuClock does not,
        // so a gap between the two elapsed spans means a suspend or a wall-clock step.
        // Magnitude, not the signed value: a backward NTP step yields a negative gap.
        var wallElapsed = Clocks.SystemClock.Now - _lastAcceptedWallAt;
        var cpuElapsed = CpuClock.Now - _lastAcceptedAt;
        return (wallElapsed - cpuElapsed).Duration();
    }

    private static BurstResult Reduce(List<Sample> samples)
    {
        // 0.5 * RTT bounds the asymmetry error, and the lowest-RTT sample has the least
        // queuing in both directions - so selecting on min-RTT beats averaging, which
        // can't reach the target on a link whose average RTT exceeds 2 * target.
        var minRtt = samples[0].Rtt;
        for (var i = 1; i < samples.Count; i++)
            minRtt = TimeSpanExt.Min(minRtt, samples[i].Rtt);

        var selected = new List<double>(samples.Count);
        foreach (var sample in samples)
            if (sample.Rtt <= RttSelectionFactor * minRtt)
                selected.Add(sample.OffsetMs);
        selected.Sort();

        var dispersion = TimeSpan.FromMilliseconds(selected[^1] - selected[0]);
        var precision = TimeSpanExt.Max(TimeSpanExt.Max(0.5 * minRtt, dispersion), TimerGranularity);
        return new BurstResult(TimeSpan.FromMilliseconds(Median(selected)), precision, minRtt, dispersion);
    }

    private static bool IsSameConnection(RpcPeerConnectionState before, RpcPeerConnectionState after)
    {
        if (before.Kind != RpcPeerConnectionStateKind.Connected)
            return false;
        if (after.Kind != RpcPeerConnectionStateKind.Connected)
            return false;
        if (before.ConnectionAttemptIndex != after.ConnectionAttemptIndex)
            return false;

        // A reroute can land on a different server instance, hence a different clock domain.
        return after.Handshake?.GetPeerChangeKind(before.Handshake) is null or RpcPeerChangeKind.Unchanged;
    }

    private static double Median(List<double> sortedValues)
    {
        var n = sortedValues.Count;
        return (n & 1) == 1
            ? sortedValues[n / 2]
            : 0.5 * (sortedValues[(n / 2) - 1] + sortedValues[n / 2]);
    }

    // Nested types

    private sealed record Sample(TimeSpan Rtt, double OffsetMs);

    private sealed record BurstResult(
        TimeSpan Offset,
        TimeSpan Precision,
        TimeSpan MinRtt,
        TimeSpan Dispersion);
}
