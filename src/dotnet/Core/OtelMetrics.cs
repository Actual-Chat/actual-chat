using System.Diagnostics.Metrics;

namespace ActualChat;

public class OtelMetrics
{
    public Histogram<float> AudioLatency { get; }
    public UpDownCounter<int> AudioStreamCount { get; }

    public OtelMetrics()
    {
        var meter = AppMeter;
        AudioLatency = meter.CreateHistogram<float>("AudioLatency", "ms", "Realtime audio recording-playback latency");
        AudioStreamCount = meter.CreateUpDownCounter<int>("AudioStreamCount", description: "Audio stream count");
    }
}
