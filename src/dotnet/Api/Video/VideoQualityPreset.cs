namespace ActualChat.Video;

// Numeric order: lower value = higher quality. Pause was removed in Step 8.5;
// pausing is now expressed via ReceiveQuality.Lowest on the playback side.
public enum VideoQualityLevel { Ultra = 0, Full = 1, High = 2, Medium = 3, Low = 4 }

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record VideoQualityPreset(
    [property: DataMember, MemoryPackOrder(0), Key(0)] VideoQualityLevel Level,
    [property: DataMember, MemoryPackOrder(1), Key(1)] int Width,
    [property: DataMember, MemoryPackOrder(2), Key(2)] int Height,
    // MemoryPackOrder(3) / Key(3) is intentionally unused — a prior schema placed Bitrate
    // there. Keep IsKeyFrameRequested at order 4 so VersionTolerant can read
    // records from older peers that still carry Bitrate at order 3.
    [property: DataMember, MemoryPackOrder(4), Key(4)] bool IsKeyFrameRequested = false,
    // Simulcast layer directive — aggregated across all peers in the stream,
    // signals to the sender which spatial/temporal enhancement layers are in
    // demand. `int.MaxValue` = no cap (produce all layers). Sender spins encoder
    // instances up/down based on these values.
    [property: DataMember, MemoryPackOrder(5), Key(5)] int MaxSpatialLayer = int.MaxValue,
    [property: DataMember, MemoryPackOrder(6), Key(6)] int MaxTemporalLayer = int.MaxValue,
    // Number of peers currently subscribed to this stream (i.e. viewers, NOT
    // counting the publisher itself). Used by the publisher to arm simulcast
    // even when its own incoming-stream count is 0 — covers the asymmetric
    // case where a publisher is the first to push and would otherwise stay
    // single-layer until another peer starts pushing back. Default 0 keeps
    // legacy peers (no field on wire) safe.
    [property: DataMember, MemoryPackOrder(7), Key(7)] int ViewerCount = 0
) {
    public static readonly VideoQualityPreset Ultra  = new(VideoQualityLevel.Ultra,  3840, 2160);
    public static readonly VideoQualityPreset Full   = new(VideoQualityLevel.Full,   1920, 1080);
    public static readonly VideoQualityPreset High   = new(VideoQualityLevel.High,   1280,  720);
    public static readonly VideoQualityPreset Medium = new(VideoQualityLevel.Medium,  960,  540);
    public static readonly VideoQualityPreset Low    = new(VideoQualityLevel.Low,     640,  360);

    public static VideoQualityPreset ForLevel(VideoQualityLevel level)
        => level switch {
            VideoQualityLevel.Ultra => Ultra,
            VideoQualityLevel.Full => Full,
            VideoQualityLevel.High => High,
            VideoQualityLevel.Medium => Medium,
            VideoQualityLevel.Low => Low,
            _ => High,
        };

    public static VideoQualityPreset? StepDown(VideoQualityLevel current)
        => current switch {
            VideoQualityLevel.Ultra => Full,
            VideoQualityLevel.Full => High,
            VideoQualityLevel.High => Medium,
            VideoQualityLevel.Medium => Low,
            _ => null, // Already at lowest
        };

    public static VideoQualityPreset? StepDown(VideoQualityLevel current, Streaming.StreamKind kind)
    {
        // Screencast floors at Medium (540p). Below that, IDE text is unreadable
        // regardless of bitrate, so pausing is preferable to sending garbage.
        if (kind == Streaming.StreamKind.Screencast && current == VideoQualityLevel.Medium)
            return null;
        return StepDown(current);
    }

    public static VideoQualityPreset? StepUp(VideoQualityLevel current)
        => current switch {
            VideoQualityLevel.Low => Medium,
            VideoQualityLevel.Medium => High,
            VideoQualityLevel.High => Full,
            VideoQualityLevel.Full => Ultra,
            _ => null, // Already at highest
        };
}
