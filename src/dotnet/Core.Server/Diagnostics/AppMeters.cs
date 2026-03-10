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

    static AppMeters()
    {
        var m = AppInstruments.Meter;
        AudioLatency = m.CreateHistogram<double>("app.audio.latency", "ms", "Real-time audio recording to playback latency");
        VideoLatency = m.CreateHistogram<double>("app.video.latency", "ms", "Video streaming latency");
        AudioStreamCount = m.CreateUpDownCounter<int>("app.audio.stream.count", null, "Audio stream count");
        VideoStreamCount = m.CreateUpDownCounter<int>("app.video.stream.count", null, "Video stream count");
        MessageCount = m.CreateCounter<long>("app.message.count", null, "Chat message count");
    }
}
