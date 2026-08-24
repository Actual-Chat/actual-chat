using System.Diagnostics.Metrics;

namespace ActualChat.Diagnostics;

// NOTE(AY): Probably it's better to move these meters to <Module>Meters.
public static class AppMeters
{
    public static readonly Histogram<double> AudioLatency;
    public static readonly Histogram<double> AvSyncError;
    public static readonly UpDownCounter<int> AudioStreamCount;
    public static readonly UpDownCounter<int> VideoStreamCount;
    public static readonly Counter<long> MessageCount;
    public static readonly Counter<long> UITextCatalogMissCount;
    public static readonly Counter<long> VerificationCodeSent;
    public static readonly Counter<long> VerificationCodeChannelSkipped;

    // Send side — sourced from RecorderStats pushed via
    // ILiveVideoStreams.ChangeRecordingQuality (1 Hz from each active recorder).
    public static readonly Histogram<double> VideoSendDropRatio;
    public static readonly Histogram<double> VideoSendAckAgeMs;
    public static readonly Histogram<int> VideoSendLayerCount;
    public static readonly Histogram<int> VideoSendThermalLevel;
    public static readonly Counter<long> VideoSendSoftwareEncode;

    // Receive side — sourced from PlaybackQualityInfo + PlaybackStreamInfo
    // pushed via ILiveVideoStreams.ChangePlaybackQuality (per decision + 5 s heartbeat).
    public static readonly Histogram<long> VideoReceiveCapacityBps;
    public static readonly Histogram<double> VideoReceiveAggregateHealth;

    // Split-attribution QC (Step 8). One histogram per leg, one counter
    // tagged with `category` for the cap walks. Recorded by the local
    // classifier on every tick — encoder/uplink come from the sender,
    // downlink/decoder are aggregated across receiver streams.
    public static readonly Histogram<double> VideoEncoderEncodeRatio;
    public static readonly Histogram<double> VideoEncoderQueueDepth;
    public static readonly Histogram<double> VideoUplinkAckAgeMs;
    public static readonly Histogram<double> VideoUplinkFloodSkipPerSec;
    public static readonly Histogram<double> VideoDownlinkLatencyMs;
    public static readonly Histogram<double> VideoDownlinkBufferUnderrunRatio;
    public static readonly Histogram<double> VideoDecoderDecodeRatio;
    public static readonly Histogram<int> VideoDecoderHangRateIn60s;
    public static readonly Counter<long> VideoLayerCapWalkReason;

    // The default OTel bounds have no negative buckets, and clock-skewed clients report
    // negative lags — without these all skew magnitudes collapse into one (-inf, 0] bucket.
    // Fine-grained through 250-2500: healthy playback sits at ~400-700 (buffer + delivery +
    // output), the A/V hold adds up to 1s on top, and its cap saturates at ~1.5-2 — the first
    // dev pull put half of all samples between 1000 and 2500 with no resolution inside.
    private static readonly double[] LagBucketBoundsMs = [
        -2500, -1000, -500, -250, -100, 0, 100, 250, 400, 550, 700, 850, 1000,
        1200, 1400, 1600, 1800, 2000, 2500, 3500, 5000, 10000,
    ];
    // Finer near zero than LagBucketBoundsMs: the perceptual limits are ~-125 ms
    // (video leads) and ~+45 ms (video trails), so both lobes need sub-100 ms resolution.
    private static readonly double[] SyncErrorBucketBoundsMs =
        [-5000, -2500, -1000, -750, -500, -250, -125, -50, 0, 50, 125, 250, 500, 750, 1000, 2500, 5000];

    static AppMeters()
    {
        var m = AppInstruments.Meter;
        AudioLatency = m.CreateHistogram(
            "app.audio.latency", "ms", "Capture-to-ear lag of the audio sample being played",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = LagBucketBoundsMs });
        AvSyncError = m.CreateHistogram(
            "app.av.sync_error", "ms", "Audio lag minus video lag for one author; >0 means video leads audio",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = SyncErrorBucketBoundsMs });
        AudioStreamCount = m.CreateUpDownCounter<int>("app.audio.stream.count", null, "Audio stream count");
        VideoStreamCount = m.CreateUpDownCounter<int>("app.video.stream.count", null, "Video stream count");
        MessageCount = m.CreateCounter<long>("app.message.count", null, "Chat message count");
        UITextCatalogMissCount = m.CreateCounter<long>(
            "app.localization.catalog_miss.count",
            null,
            "UI strings with no catalog entry, which fall back to AI translation; tags: language, kind");
        VerificationCodeSent = m.CreateCounter<long>(
            "app.verification_code.sent", null, "Verification codes sent, tagged with the delivery channel");
        VerificationCodeChannelSkipped = m.CreateCounter<long>(
            "app.verification_code.channel_skipped", null,
            "Channels that didn't deliver a verification code; "
            + "reason tag: blocked | unconfigured | prefix | declined | failed");

        VideoSendDropRatio = m.CreateHistogram<double>(
            "app.video.send.drop_ratio", "ratio", "Sender RpcStream dropped-frame ratio over the last 1 s window");
        VideoSendAckAgeMs = m.CreateHistogram<double>(
            "app.video.send.ack_age", "ms", "Wall-clock age of the most recent sender ACK");
        VideoSendLayerCount = m.CreateHistogram<int>(
            "app.video.send.layer_count", "layers", "Effective layer count on the recording client");
        VideoSendThermalLevel = m.CreateHistogram<int>(
            "app.video.send.thermal_level", "level", "Recording client's thermal level (0 = nominal … 3 = critical)");
        VideoSendSoftwareEncode = m.CreateCounter<long>(
            "app.video.send.software_encode", null, "Recording quality reports from clients encoding in software");

        VideoReceiveCapacityBps = m.CreateHistogram<long>(
            "app.video.receive.capacity", "By/s", "Estimated client-wide incoming video capacity");
        VideoReceiveAggregateHealth = m.CreateHistogram<double>(
            "app.video.receive.aggregate_health", "ratio", "Byte-weighted aggregate playback health verdict (-1..+1)");

        VideoEncoderEncodeRatio = m.CreateHistogram<double>(
            "app.video.encoder.encode_ratio", "ratio",
            "Encoder throughput deficit, EMA-smoothed (0 = keeps pace with capture, 1 = emits nothing)");
        VideoEncoderQueueDepth = m.CreateHistogram<double>(
            "app.video.encoder.queue_depth", "frames",
            "WebCodecs encoder pending submissions, peak across layers (EMA)");
        VideoUplinkAckAgeMs = m.CreateHistogram<double>(
            "app.video.uplink.ack_age", "ms", "Sender→server ACK staleness");
        VideoUplinkFloodSkipPerSec = m.CreateHistogram<double>(
            "app.video.uplink.flood_skip", "events/s", "Sender FloodGate skip events per second");
        VideoDownlinkLatencyMs = m.CreateHistogram<double>(
            "app.video.downlink.latency", "ms",
            "Server→receiver above-floor latency (EMA, after sliding-min skew baseline)");
        VideoDownlinkBufferUnderrunRatio = m.CreateHistogram<double>(
            "app.video.downlink.buffer_underrun_ratio", "ratio",
            "Fraction of receiver buffer pushes seen below targetSpan/3 (EMA)");
        VideoDecoderDecodeRatio = m.CreateHistogram<double>(
            "app.video.decoder.decode_ratio", "ratio", "WebCodecs decode wall-clock / frame budget (receiver, EMA)");
        VideoDecoderHangRateIn60s = m.CreateHistogram<int>(
            "app.video.decoder.hang_rate", "events", "Receiver decoder hang-watchdog fires in the last 60 s");
        VideoLayerCapWalkReason = m.CreateCounter<long>(
            "app.video.layer_cap.walk", "events",
            "Per-category layer-cap walks; tag `category` = encoder|uplink|downlink|decoder, `direction` = up|down");
    }
}
