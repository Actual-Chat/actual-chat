using System.Diagnostics.Metrics;

namespace ActualChat.Streaming.Diagnostics;

public static class StreamingMeters
{
    // Frame processing timing (microseconds to avoid sub-ms rounding)
    public static readonly Histogram<double> VideoFrameDeserializeDuration;
    public static readonly Histogram<double> VideoFrameSerializeDuration;

    // Frame sizes
    public static readonly Histogram<int> VideoFrameSizeBytes;

    // Per-stream consumer tracking
    public static readonly UpDownCounter<int> VideoActiveConsumers;

    // Throughput
    public static readonly Counter<long> VideoFramesReceived;
    public static readonly Counter<long> VideoFramesSent;
    public static readonly Counter<long> VideoBytesReceived;
    public static readonly Counter<long> VideoBytesSent;

    static StreamingMeters()
    {
        var m = StreamingInstruments.Meter;
        VideoFrameDeserializeDuration = m.CreateHistogram<double>(
            "streaming.video.frame.deserialize_duration", "us",
            "Time to deserialize a video frame from MessagePack");
        VideoFrameSerializeDuration = m.CreateHistogram<double>(
            "streaming.video.frame.serialize_duration", "us",
            "Time to serialize a video frame to MessagePack");
        VideoFrameSizeBytes = m.CreateHistogram<int>(
            "streaming.video.frame.size_bytes", "By",
            "Video frame data size in bytes");
        VideoActiveConsumers = m.CreateUpDownCounter<int>(
            "streaming.video.active_consumers", null,
            "Number of active video consumers");
        VideoFramesReceived = m.CreateCounter<long>(
            "streaming.video.frames_received", null,
            "Total video frames received from producers");
        VideoFramesSent = m.CreateCounter<long>(
            "streaming.video.frames_sent", null,
            "Total video frames sent to consumers");
        VideoBytesReceived = m.CreateCounter<long>(
            "streaming.video.bytes_received", "By",
            "Total video bytes received from producers");
        VideoBytesSent = m.CreateCounter<long>(
            "streaming.video.bytes_sent", "By",
            "Total video bytes sent to consumers");
    }
}
