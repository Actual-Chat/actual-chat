using ActualChat.Bandwidth;
using ActualChat.Hosting;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Client-side video quality controller. Drives recording and playback decisions
/// from local health signals; pushes summaries to the server via
/// <see cref="ILiveVideoStreams.ChangeRecordingQuality"/> and
/// <see cref="ILiveVideoStreams.ChangePlaybackQuality"/>.
/// Implementation split across the .Recording, .Playback, and .Debug partials.
/// </summary>
public sealed partial class VideoQualityUI : UIWorkerBase<AppUIHub>
{
    private const int ColdStartTicks = 2; // ~2 s of grace at 1 Hz
    // Stream-age-tiered evaluation cadence for both rec and playback QC.
    // Health snapshots arrive at 1 Hz; we throttle the controller's
    // decide+push step on top of that to avoid thrash while a fresh stream
    // is still settling and to cut steady-state traffic later. The 3 s
    // startup cooldown covers the L2-keyframe wait while keeping upgrades
    // responsive once the first real playback stats arrive.
    private static readonly TimeSpan QcStartupCooldown = TimeSpan.FromSeconds(3);
    // Initial outbound camera tiers at openGate; the ramp earns tiers beyond this.
    private const int SoftStartCameraLayers = 3;
    private static readonly TimeSpan QcSettlingInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan QcSettlingDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan QcSteadyInterval = TimeSpan.FromSeconds(5);

    private bool _wasConnected = true;
    private int _coldStartTicksRemaining;
    private readonly TaskCompletionSource _whenActuallyUsed = new();

    private ConnectivityUI ConnectivityUI => Hub.ConnectivityUI;
    private BrowserInfo BrowserInfo => Hub.BrowserInfo;
    private BackgroundStateTracker BackgroundStateTracker
        => field ??= Services.GetRequiredService<BackgroundStateTracker>();
    private ThermalTracker ThermalTracker
        => field ??= Services.GetRequiredService<ThermalTracker>();
    private MomentClock SystemClock => Hub.Clocks.SystemClock;
    private ILiveVideoStreams LiveVideoStreams
        => field ??= Services.GetRequiredService<ILiveVideoStreams>();

    public VideoQualityUI(AppUIHub hub) : base(hub)
    {
        var isMobile = BrowserInfo.IsMobile || HostInfo.AppKind.IsMobile();
        var deviceCameraCap = isMobile
            ? Math.Min(2, VideoLayerDef.CameraLayers.Length)
            : VideoLayerDef.CameraLayers.Length;
        var screencastCap = VideoLayerDef.ScreenCastLayers.Length;
        // Soft-start the outbound camera health caps below the device ceiling so
        // the top tier (e.g. 1080) is only added by the bump-quality ramp once the
        // lower tiers run smoothly — not encoded from the first frame.
        var softCameraStart = Math.Min(SoftStartCameraLayers, deviceCameraCap);
        _outboundLayers = new LayerCap(deviceCameraCap, screencastCap);
        _outboundEncodingCap = new EncodingCap(
            new LayerCap(deviceCameraCap, screencastCap, softCameraStart),
            new EncodingCapConfig(
                EncodeDeficitBad: Constants.Video.EncBadDeficit,
                EncodeDeficitGood: Constants.Video.EncOkDeficit));
        _outboundBandwidthCap = new BandwidthCap(
            new LayerCap(deviceCameraCap, screencastCap, softCameraStart),
            new BandwidthCapConfig());
        _outboundBwEstimator = new BandwidthEstimator(
            new BandwidthEstimatorConfig(Constants.Video.InitialOutboundCeilingBps));
        _inboundBwEstimator = new BandwidthEstimator(
            new BandwidthEstimatorConfig(Constants.Video.InitialInboundCeilingBps));
        _thermalCap = new ThermalCap(deviceCameraCap, screencastCap, ThermalCapConfig.Default);
        this.Start();
    }

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await _whenActuallyUsed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        var chains = new[] {
            AsyncChain.From(WatchConnectivity),
            AsyncChain.From(WatchVideoPanelMode),
            AsyncChain.From(WatchBackgroundState),
            AsyncChain.From(WatchThermal),
            AsyncChain.From(RunPlaybackQualityKeepAlive),
            AsyncChain.From(LoadDebugSettings),
        };
        var retryDelays = RetryDelaySeq.Exp(0.5, 1);
        await chains
            .Select(chain => chain.Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log))
            .RunIsolated(cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private async Task WatchVideoPanelMode(CancellationToken cancellationToken)
    {
        // Pause/resume requests must propagate within a tick — without this, the
        // override sits idle until the next 5s steady-state QC re-evaluation.
        // The cached mode also tells GetFreshPlaybackEntries to keep entries
        // alive while paused (no inbound frames → no OnPlaybackStats refresh).
        var cMode = await Computed
            .Capture(() => Hub.ChatVideoUI.GetVideoPanelMode(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var change in cMode.Changes(cancellationToken).ConfigureAwait(false)) {
            bool isResumingPlayback;
            lock (_playbackLock) {
                isResumingPlayback = IsPlaybackPaused(_currentPanelMode) && !IsPlaybackPaused(change.Value);
                _currentPanelMode = change.Value;
                if (isResumingPlayback)
                    RefreshPlaybackEntriesLastSeenLocked();
            }
            await RecomputePlaybackQuality(
                PlaybackQualityReason.ActiveSetChanged,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsPlaybackPaused(VideoPanelMode mode)
        => mode is VideoPanelMode.Hidden or VideoPanelMode.Collapsed;

    private async Task WatchThermal(CancellationToken cancellationToken)
    {
        // Thermal escalation must bite within a tick — waiting for the steady
        // 5 s QC interval lets the device heat further before we back off.
        var cState = ThermalTracker.Level.Computed;
        await foreach (var (level, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            if (!_thermalCap.Tick(SystemClock.Now, level))
                continue;

            Log.LogInformation("WatchThermal: thermal cap level -> {Level}", _thermalCap.Level);
            // Inbound reacts here; the encoder cap/fps ceiling is applied by the
            // outbound tick, so force it to evaluate on the next stats callback
            // instead of waiting out the steady 5 s QC interval.
            _forceOutboundEval = true;
            await RecomputePlaybackQuality(
                PlaybackQualityReason.Backoff,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WatchBackgroundState(CancellationToken cancellationToken)
    {
        // Hidden tab / backgrounded app must pause inbound video within a tick —
        // frames otherwise keep arriving and decoding invisibly, which is the
        // single largest pointless battery drain on mobile.
        var cState = BackgroundStateTracker.IsBackground.Computed;
        await foreach (var (isBackground, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            bool isResumingPlayback;
            lock (_playbackLock) {
                isResumingPlayback = _isBackground && !isBackground;
                _isBackground = isBackground;
                if (isResumingPlayback)
                    RefreshPlaybackEntriesLastSeenLocked();
            }
            await RecomputePlaybackQuality(
                PlaybackQualityReason.ActiveSetChanged,
                cancellationToken).ConfigureAwait(false);
            // A paused-then-resumed pipeline reliably needs a fresh keyframe
            // anchor and often a fresh decoder (background GPU deprioritization);
            // restarting now beats waiting 4-7 s for the stall watchdogs.
            if (isResumingPlayback)
                await RestartActivePlayersForResume(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WatchConnectivity(CancellationToken cancellationToken)
    {
        var cState = ConnectivityUI.IsConnected.Computed;
        await foreach (var (isConnected, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            if (!_wasConnected && isConnected) {
                _coldStartTicksRemaining = ColdStartTicks;
                _outboundStartedAt = default;
                _outboundLastEvalAt = default;
                _lastAppliedTargetByKind.Clear();
                _lastEncodedSampleByKind.Clear();
                _lastAckedSampleByKind.Clear();
                _outboundProbe.Reset();
                lock (_playbackLock) {
                    _playbackStartedAt.Clear();
                    _playbackLastEvalAt.Clear();
                }
            }
            _wasConnected = isConnected;
        }
    }

    private static bool IsEvaluationDue(CpuTimestamp startedAt, CpuTimestamp lastEvalAt, bool force = false)
    {
        // Cooldown is unconditional even with force=true: layer requests during
        // the L2-keyframe wait + EMA(10) ramp-up are based on noisy signals.
        var age = startedAt.Elapsed;
        if (age < QcStartupCooldown)
            return false;
        if (force)
            return true;
        var sinceLast = lastEvalAt.Elapsed;
        var required = age < QcSettlingDuration ? QcSettlingInterval : QcSteadyInterval;
        return sinceLast >= required;
    }

    // ----- Decision log -----

    private const int DecisionLogCapacity = 10;
    private readonly Lock _decisionLogLock = new();
    private readonly Queue<QualityDecisionEntry> _outboundDecisionLog = new(DecisionLogCapacity);
    private readonly Queue<QualityDecisionEntry> _inboundDecisionLog = new(DecisionLogCapacity);

    public IReadOnlyCollection<QualityDecisionEntry> OutboundDecisionLog {
        get { lock (_decisionLogLock) return _outboundDecisionLog.ToArray(); }
    }
    public IReadOnlyCollection<QualityDecisionEntry> InboundDecisionLog {
        get { lock (_decisionLogLock) return _inboundDecisionLog.ToArray(); }
    }

    internal void AppendOutboundDecision(QualityDecisionEntry entry)
    {
        lock (_decisionLogLock) {
            _outboundDecisionLog.Enqueue(entry);
            while (_outboundDecisionLog.Count > DecisionLogCapacity)
                _outboundDecisionLog.Dequeue();
        }
    }

    internal void AppendInboundDecision(QualityDecisionEntry entry)
    {
        lock (_decisionLogLock) {
            _inboundDecisionLog.Enqueue(entry);
            while (_inboundDecisionLog.Count > DecisionLogCapacity)
                _inboundDecisionLog.Dequeue();
        }
    }

    public sealed record QualityDecisionEntry(
        Moment At,
        HealthVerdict SignalA,        // outbound: Encoder | inbound: Downlink
        HealthVerdict SignalB,        // outbound: Uplink  | inbound: Decoder
        BandwidthVerdict BweVerdict,
        string CapChange,             // "" when no cap moved
        string Reason,                // dominant trigger
        string RawValuesA,
        string RawValuesB,
        string RawValuesBw);
}
