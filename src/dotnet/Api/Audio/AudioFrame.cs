namespace ActualChat.Audio;

/// <summary>
/// Represents a single frame of audio data.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class AudioFrame : MediaFrame
{
    // Override slots reuse the base's reserved indices 1/2/3 (MediaFrame.Data sits at slot 0;
    // slots 4..9 stay reserved for future MediaFrame additions).
    //
    // [PropertyShape] (without Ignore) RE-INCLUDES the override in PolyType's emitted shape.
    // The base declares these abstract members with [PropertyShape(Ignore = true)] so
    // PolyType's [Key]-consistency analyzer is happy with a base that only keys `Data`;
    // without re-attributing the override, PolyType inherits the Ignore and silently drops
    // the property from the wire — frames round-trip with default Offset/Duration/IsKeyFrame.
    [DataMember(Order = 4), MemoryPackOrder(4), Key(1), PropertyShape]
    public override TimeSpan Offset { get; init; }
    [Key(2), PropertyShape]
    public override TimeSpan Duration { get; init; } = Constants.Audio.OpusFrameDuration;
    [Key(3), PropertyShape]
    public override bool IsKeyFrame { get; init; } = true;

    /// <summary>
    /// Cached serialized bytes for zero-copy forwarding and serialize-once fan-out.
    /// Populated at ingress by <see cref="CachingAudioFrameFormatter"/>.<c>Deserialize</c>
    /// (single-writer: the RPC read loop of the producer's peer). Fan-out consumers read
    /// only — the memoizer's <c>TrySetResult</c> establishes the happens-before edge that
    /// makes the write visible. Do not add writers outside the ingress path.
    /// Backed by a plain GC-managed <c>byte[]</c>; <see cref="MediaFrame.Data"/> is a slice
    /// into the same array, so the array lives as long as any consumer holds this frame.
    /// </summary>
    [IgnoreDataMember, MemoryPackIgnore, PropertyShape(Ignore = true)]
    internal ReadOnlyMemory<byte> SerializedData { get; set; }
}
