namespace ActualChat.Video;

// Numeric order: lower value = higher quality. Preserved so `level < maxQuality`
// comparisons in StreamLatencyStore express "is this better than the cap".
public enum VideoQualityLevel { Ultra = 0, Full = 1, High = 2, Medium = 3, Low = 4, Paused = 5 }

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record VideoQualityPreset(
    [property: DataMember, MemoryPackOrder(0)] VideoQualityLevel Level,
    [property: DataMember, MemoryPackOrder(1)] int Width,
    [property: DataMember, MemoryPackOrder(2)] int Height,
    // MemoryPackOrder(3) is intentionally unused — a prior schema placed Bitrate
    // there. Keep IsKeyFrameRequested at order 4 so VersionTolerant can read
    // records from older peers that still carry Bitrate at order 3.
    [property: DataMember, MemoryPackOrder(4)] bool IsKeyFrameRequested = false
) {
    public static readonly VideoQualityPreset Ultra  = new(VideoQualityLevel.Ultra,  3840, 2160);
    public static readonly VideoQualityPreset Full   = new(VideoQualityLevel.Full,   1920, 1080);
    public static readonly VideoQualityPreset High   = new(VideoQualityLevel.High,   1280,  720);
    public static readonly VideoQualityPreset Medium = new(VideoQualityLevel.Medium,  960,  540);
    public static readonly VideoQualityPreset Low    = new(VideoQualityLevel.Low,     640,  360);
    public static readonly VideoQualityPreset Paused = new(VideoQualityLevel.Paused, 0, 0);

    public static VideoQualityPreset ForLevel(VideoQualityLevel level)
        => level switch {
            VideoQualityLevel.Ultra => Ultra,
            VideoQualityLevel.Full => Full,
            VideoQualityLevel.High => High,
            VideoQualityLevel.Medium => Medium,
            VideoQualityLevel.Low => Low,
            VideoQualityLevel.Paused => Paused,
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
