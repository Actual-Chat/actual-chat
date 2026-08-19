namespace ActualChat.Streaming.Services;

/// <summary>
/// Range guards for the numbers clients report about their own streaming.
/// Nothing validates them on the wire, so NaN, infinities and absurd
/// magnitudes must be dropped or clamped before they reach meters or
/// layer-selection decisions.
/// </summary>
public static class ClientStats
{
    public const int MaxLayerId = 30; // VideoStreamingBackend packs layer ids into an int mask
    public const int MaxLayerCount = 8;
    public const int MaxStreamCount = 64;
    public const double MaxAckAgeMs = 600_000;
    public const long MaxByteRate = 1_250_000_000; // 10 Gbit/s
    public static readonly TimeSpan MaxAudioLatency = TimeSpan.FromMinutes(1);

    public static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    public static double? Ratio(double value)
        => IsFinite(value) ? value.Clamp(0, 1) : null;

    public static double? AckAgeMs(double value)
        => IsFinite(value) ? value.Clamp(-1, MaxAckAgeMs) : null;

    public static int LayerCount(int value)
        => value.Clamp(0, MaxLayerCount);

    public static long ByteRate(long value)
        => value.Clamp(0, MaxByteRate);

    public static TimeSpan AudioLatency(TimeSpan value)
        // Signed: a stale-behind client clock reports negative latency — the desync signal this meter surfaces
        => value.Clamp(-MaxAudioLatency, MaxAudioLatency);

    public static TimeSpan PlaybackLag(TimeSpan value)
        // Signed like AudioLatency: negative is a real reading, not a fault
        => value.Clamp(-MaxAudioLatency, MaxAudioLatency);

    public static ReceiveQuality Quality(ReceiveQuality quality)
        => quality.LayerId <= MaxLayerId
            ? quality
            : new ReceiveQuality(MaxLayerId, quality.IsThumbnail);
}
