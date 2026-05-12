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
    // is still settling and to cut steady-state traffic later. The 5 s
    // startup cooldown covers the L2-keyframe wait (~3 s) plus EMA(10)
    // ramp-up so the first eval lands on a settled buffer signal.
    private static readonly TimeSpan QcStartupCooldown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan QcSettlingInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan QcSettlingDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan QcSteadyInterval = TimeSpan.FromSeconds(5);

    private bool _wasConnected = true;
    private int _coldStartTicksRemaining;
    private readonly TaskCompletionSource _whenActuallyUsed = new();

    private ConnectivityUI ConnectivityUI => Hub.ConnectivityUI;
    private BrowserInfo BrowserInfo => Hub.BrowserInfo;
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
        _outboundLayers = new LayerCap(deviceCameraCap, screencastCap);
        _outboundEncodingCap = new EncodingCap(
            new LayerCap(deviceCameraCap, screencastCap),
            new EncodingCapConfig(
                EncodeRatioBad: Constants.Video.EncBadRatio,
                EncodeRatioGood: Constants.Video.EncOkRatio + 0.2));
        _outboundBandwidthCap = new BandwidthCap(
            new LayerCap(deviceCameraCap, screencastCap),
            new BandwidthCapConfig());
        _outboundBwEstimator = new BandwidthEstimator(
            new BandwidthEstimatorConfig(Constants.Video.InitialOutboundCeilingBps));
        _inboundBwEstimator = new BandwidthEstimator(
            new BandwidthEstimatorConfig(Constants.Video.InitialInboundCeilingBps));
        this.Start();
    }

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await _whenActuallyUsed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        var chains = new[] {
            AsyncChain.From(WatchConnectivityEdges),
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

    private async Task WatchConnectivityEdges(CancellationToken cancellationToken)
    {
        var cState = ConnectivityUI.IsConnected.Computed;
        await foreach (var (isConnected, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            if (!_wasConnected && isConnected) {
                _coldStartTicksRemaining = ColdStartTicks;
                _outboundStartedAt = default;
                _outboundLastEvalAt = default;
                _lastAppliedTargetByKind.Clear();
                _lastEncodedSampleByKind.Clear();
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
}
