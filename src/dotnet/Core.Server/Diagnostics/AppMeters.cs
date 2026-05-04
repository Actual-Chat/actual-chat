using System.Diagnostics.Metrics;

namespace ActualChat.Diagnostics;

// NOTE(AY): Probably it's better to move these meters to <Module>Meters.
public static class AppMeters
{
    public static readonly Histogram<double> AudioLatency;
    public static readonly Histogram<double> VideoLatency;
    public static readonly UpDownCounter<int> AudioStreamCount;
    public static readonly UpDownCounter<int> VideoStreamCount;
    public static readonly Counter<long> MessageCount;

    // Send side — sourced from RecorderHealthSnapshot pushed via
    // ILiveVideoStreams.ChangeRecordingQuality (1 Hz from each active recorder).
    public static readonly Histogram<double> VideoSendEncodeRatio;
    public static readonly Histogram<double> VideoSendDropRatio;
    public static readonly Histogram<double> VideoSendAckAgeMs;
    public static readonly Histogram<int> VideoSendLayerCount;

    // Receive side — sourced from PlaybackQualityInfo + PlaybackStreamInfo
    // pushed via ILiveVideoStreams.ChangePlaybackQuality (per decision + 5 s heartbeat).
    public static readonly Histogram<long> VideoReceiveCapacityBps;
    public static readonly Histogram<double> VideoReceiveAggregateHealth;
    public static readonly Counter<long> VideoReceiveKeyframeSkips;
    public static readonly Histogram<double> VideoReceiveDecoderQueue;

    static AppMeters()
    {
        var m = AppInstruments.Meter;
        AudioLatency = m.CreateHistogram<double>("app.audio.latency", "ms", "Real-time audio recording to playback latency");
        VideoLatency = m.CreateHistogram<double>("app.video.latency", "ms", "Video streaming latency");
        AudioStreamCount = m.CreateUpDownCounter<int>("app.audio.stream.count", null, "Audio stream count");
        VideoStreamCount = m.CreateUpDownCounter<int>("app.video.stream.count", null, "Video stream count");
        MessageCount = m.CreateCounter<long>("app.message.count", null, "Chat message count");

        VideoSendEncodeRatio = m.CreateHistogram<double>(
            "app.video.send.encode_ratio", "ratio", "P90 encoder cost / frame budget on the recording client");
        VideoSendDropRatio = m.CreateHistogram<double>(
            "app.video.send.drop_ratio", "ratio", "Sender RpcStream dropped-frame ratio over the last 1 s window");
        VideoSendAckAgeMs = m.CreateHistogram<double>(
            "app.video.send.ack_age", "ms", "Wall-clock age of the most recent sender ACK");
        VideoSendLayerCount = m.CreateHistogram<int>(
            "app.video.send.layer_count", "layers", "Effective simulcast layer count on the recording client");

        VideoReceiveCapacityBps = m.CreateHistogram<long>(
            "app.video.receive.capacity", "By/s", "Estimated client-wide incoming video capacity");
        VideoReceiveAggregateHealth = m.CreateHistogram<double>(
            "app.video.receive.aggregate_health", "ratio", "Byte-weighted aggregate playback health verdict (-1..+1)");
        VideoReceiveKeyframeSkips = m.CreateCounter<long>(
            "app.video.receive.keyframe_skips", "events", "Keyframe-anchored evictions in the receive buffer");
        VideoReceiveDecoderQueue = m.CreateHistogram<double>(
            "app.video.receive.decoder_queue", "depth", "EMA decoder queue depth on the receiving client");
    }
}
