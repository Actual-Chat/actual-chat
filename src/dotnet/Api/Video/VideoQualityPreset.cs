using MemoryPack;

namespace ActualChat.Video;

public enum VideoQualityLevel { Full = 0, High = 1, Medium = 2, Low = 3 }

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record VideoQualityPreset(
    [property: DataMember, MemoryPackOrder(0)] VideoQualityLevel Level,
    [property: DataMember, MemoryPackOrder(1)] int Width,
    [property: DataMember, MemoryPackOrder(2)] int Height,
    [property: DataMember, MemoryPackOrder(3)] int Bitrate
) {
    public static readonly VideoQualityPreset Full   = new(VideoQualityLevel.Full,   1920, 1080, 8_000_000);
    public static readonly VideoQualityPreset High   = new(VideoQualityLevel.High,   1280,  720, 4_000_000);
    public static readonly VideoQualityPreset Medium = new(VideoQualityLevel.Medium,  960,  540, 2_500_000);
    public static readonly VideoQualityPreset Low    = new(VideoQualityLevel.Low,     640,  360, 1_000_000);

    public static VideoQualityPreset ForLevel(VideoQualityLevel level)
        => level switch {
            VideoQualityLevel.Full => Full,
            VideoQualityLevel.High => High,
            VideoQualityLevel.Medium => Medium,
            VideoQualityLevel.Low => Low,
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
