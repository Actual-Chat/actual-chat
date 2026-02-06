namespace ActualChat.Transcription;

/// <summary>
/// Result of a remap operation, exposing both the map and similarity score.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct LinearMapRemapResult
{
    public const double MinorEditSimilarityThreshold = 0.7;
    public static readonly LinearMapRemapResult Zero = new() { Map = LinearMap.Zero, Similarity = 0 };

    public LinearMap Map { get; init; }
    public double Similarity { get; init; }
}
