using ActualChat.Hosting;
using ActualChat.Video;

namespace ActualChat.Streaming;

public enum RecordingQualityReason
{
    Stable = 0,
    Climb,
    Backoff,
    StuckAtFloor,
    ColdStartTick,
    ReconnectPush,
}

/// <summary>
/// Recorder controller's intended layer count and the count actually
/// applied to the encoder. Sent to the server purely as a metric.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record RecordingQualityState(
    [property: DataMember(Order = 0), Key(0)] int TargetLayerCount,
    [property: DataMember(Order = 1), Key(1)] int EffectiveLayerCount);

/// <summary>
/// Wire payload accompanying a recording quality decision. Slim: only
/// what the server-side telemetry uses (VideoSendDropRatio, VideoSendAckAgeMs).
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record RecordingQualityInfo(
    [property: DataMember(Order = 0), Key(0)] RecordingQualityReason Reason,
    [property: DataMember(Order = 1), Key(1)] double SenderFrameDropRatioEma,
    [property: DataMember(Order = 2), Key(2)] double LastAckAgeMs,
    [property: DataMember(Order = 3), Key(3)] ThermalLevel ThermalLevel = default,
    [property: DataMember(Order = 4), Key(4)] bool IsHardwareAccelerated = false);

/// <summary>
/// Per-tick recorder stats — CLIENT-LOCAL only, never serialized to the
/// wire. Consumed by the local QC classifier and the outbound-side video
/// diagnostics. Pruned to only what one of those two consumers reads.
/// </summary>
public sealed record RecorderStats(
    // Encoder THROUGHPUT DEFICIT, 0..1. Computed on the main thread as
    // 1 - (bundlesEncodedPerSec / framesCapturedPerSec), EMA-smoothed.
    // 0 = encoder keeps pace with the source; 1 = encoder emits nothing.
    // Drives QC layer demotion via SenderHealthClassifier + EncodingCap.
    // NOT pipeline saturation — a queue-full encoder that still emits at
    // source rate is healthy and should not be demoted; this metric only
    // moves when emit rate actually falls behind capture rate.
    double EncodeDeficitEma,
    // Bundles dropped anywhere in the sender pipeline / (bundles dropped +
    // bundles shipped), EMA-smoothed.
    double SenderFrameDropRatioEma,
    double LastAckAgeMs,
    bool IsConnected,
    bool IsPeerConnected,
    // Cumulative per-stage drop counts since the recording run started.
    // Powers the per-stream FPS breakdown in video diagnostics.
    IReadOnlyDictionary<FrameDropStage, int> DropTrace,
    // Cumulative bundles successfully shipped to the wire since the run
    // started. Powers FPS (= delta-per-second) for the diagnostics row.
    int BundlesShipped,
    // Cumulative bundles encoded (one per source moment) emerging from
    // the encode operator. Isolates encoder throughput from wire
    // throughput — if wire back-pressures, BundlesShipped lags
    // BundlesEncoded; both drop together if encoder is the bottleneck.
    int BundlesEncoded,
    // Cumulative encoded bytes (sum across layers). Drives the outbound
    // "kbps" display in the diagnostics modal.
    long BytesEncoded,
    // Health-classifier inputs (Step 4 of split-attribution QC plan).
    double EncodeQueueDepthEma,
    double WireQueueDepthEma,
    double FloodGateSkipPerSec,
    int PeerReconnectStreak,
    int EncoderRestartStreakIn60s,
    bool IsTabBackgrounded,
    // Cumulative bytes the peer acknowledged (delivered) on the wire. While the
    // wire queue is backlogged the delta over time is the link drain rate — a
    // true measured uplink capacity, used to re-anchor the bandwidth ceiling.
    long WireAckedBytes = 0,
    // Per-stage wall-time (ms/bundle, window mean) for the outbound diagnostics
    // "where time goes" rows. -1 until the first sample window. Downscale is the
    // GPU resize stage; Encode is the HW encoder.
    double EncodeTimeMsMean = -1,
    double DownscaleTimeMsMean = -1,
    double DownscaleTimeMsMax = -1,
    // Cumulative synthetic frames re-emitted during capture idle (static
    // screencast content) — the keepAlive operator's activity counter.
    int KeepAliveFramesInjected = 0,
    // Whether the active encoder is hardware-accelerated (from CodecInfo).
    // A software-encoding device throttles thermally far sooner.
    bool IsHardwareAccelerated = false,
    // Windowed-min ack round trip on the publisher RpcStream (ms, -1 until
    // sampled) ≈ propagation RTT — the baseline for RTT-relative QC gates.
    double WireMinRttMs = -1,
    // EMA of the RpcStream local ring occupancy (bundles, -1 until sampled) —
    // the earliest outbound-backpressure signal (the ring absorbs ~4s of
    // backlog before the Denque behind WireQueueDepthEma even starts filling).
    double WireRingDepthEma = -1)
{
    private static readonly IReadOnlyDictionary<FrameDropStage, int> EmptyDropTrace
        = new Dictionary<FrameDropStage, int>();

    public static RecorderStats Empty { get; } =
        new(0, 0, 0, IsConnected: false, IsPeerConnected: false, EmptyDropTrace, 0,
            BundlesEncoded: 0,
            BytesEncoded: 0,
            EncodeQueueDepthEma: -1,
            WireQueueDepthEma: -1,
            FloodGateSkipPerSec: 0,
            PeerReconnectStreak: 0,
            EncoderRestartStreakIn60s: 0,
            IsTabBackgrounded: false);
}
