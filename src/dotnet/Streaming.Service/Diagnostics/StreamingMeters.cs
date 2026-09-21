using System.Diagnostics.Metrics;

namespace ActualChat.Streaming.Diagnostics;

public static class StreamingMeters
{
    // How far behind the speech each live pipeline stage runs, per utterance (seconds)
    public static readonly Histogram<double> TranscriptLag;
    public static readonly Histogram<double> DubLag;
    public static readonly Histogram<double> DubTtsOpenDelay;
    public static readonly Histogram<double> DubTtsFirstAudio;
    public static readonly Histogram<double> DubDecisionDelay;

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

    // Live-session-ended events that could not be enqueued (the session still closes)
    public static readonly Counter<long> LiveSessionEndedDropped;

    static StreamingMeters()
    {
        var m = StreamingInstruments.Meter;
        TranscriptLag = m.CreateHistogram<double>(
            "streaming.transcript.lag", "s",
            "Seconds a live transcript runs behind the speech (kind=text|stable)");
        DubLag = m.CreateHistogram<double>(
            "streaming.dub.lag", "s",
            "Seconds a live dub stage runs behind the speech (stage=translated|spoken|first_word|mixed)");
        DubTtsOpenDelay = m.CreateHistogram<double>(
            "streaming.dub.tts_open_delay", "s",
            "Seconds from the first chunk handed to TTS to the first text sent on a TTS stream");
        DubTtsFirstAudio = m.CreateHistogram<double>(
            "streaming.dub.tts_first_audio", "s",
            "Seconds from the first text sent on a TTS stream to its first audio");
        DubDecisionDelay = m.CreateHistogram<double>(
            "streaming.dub.decision_delay", "s",
            "Seconds from a dub request to its dub/no-dub decision");
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
        LiveSessionEndedDropped = m.CreateCounter<long>(
            "live_session.ended.dropped", null, "LiveSessionEndedEvent enqueue failures");
    }
}
