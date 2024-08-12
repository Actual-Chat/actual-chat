using System.Diagnostics.Metrics;

namespace ActualChat.Diagnostics;

// NOTE(AY): Probably it's better to move these meters to <Module>Meters.
public static class AppMeters
{
    public static readonly Histogram<float> AudioLatency;
    public static readonly UpDownCounter<int> AudioStreamCount;
    public static readonly Counter<long> MessageCount;

    static AppMeters()
    {
        var m = AppInstruments.Meter;
        AudioLatency = m.CreateHistogram<float>("AudioLatency", "ms", "Real-time audio recording to playback latency");
        AudioStreamCount = m.CreateUpDownCounter<int>("AudioStreamCount", null, "Audio stream count");
        MessageCount = m.CreateCounter<long>("MessageCount", null, "Chat message count");
    }
}
