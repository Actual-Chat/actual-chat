namespace ActualChat.Video;

public enum VideoQualityLevel { Full = 0, High = 1, Medium = 2, Low = 3, Paused = 4 }

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
    public static readonly VideoQualityPreset Full   = new(VideoQualityLevel.Full,   1920, 1080);
    public static readonly VideoQualityPreset High   = new(VideoQualityLevel.High,   1280,  720);
    public static readonly VideoQualityPreset Medium = new(VideoQualityLevel.Medium,  960,  540);
    public static readonly VideoQualityPreset Low    = new(VideoQualityLevel.Low,     640,  360);
    public static readonly VideoQualityPreset Paused = new(VideoQualityLevel.Paused, 0, 0);

    public static VideoQualityPreset ForLevel(VideoQualityLevel level)
        => level switch {
            VideoQualityLevel.Full => Full,
            VideoQualityLevel.High => High,
            VideoQualityLevel.Medium => Medium,
            VideoQualityLevel.Low => Low,
            VideoQualityLevel.Paused => Paused,
            _ => High,
        };

    public static VideoQualityPreset? StepDown(VideoQualityLevel current)
        => current switch {
            VideoQualityLevel.Full => High,
            VideoQualityLevel.High => Medium,
            VideoQualityLevel.Medium => Low,
            _ => null, // Already at lowest
        };

    public static VideoQualityPreset? StepUp(VideoQualityLevel current)
        => current switch {
            VideoQualityLevel.Low => Medium,
            VideoQualityLevel.Medium => High,
            VideoQualityLevel.High => Full,
            _ => null, // Already at highest
        };
}
