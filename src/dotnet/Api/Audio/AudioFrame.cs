namespace ActualChat.Audio;

/// <summary>
/// Represents a single frame of audio data.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
[MessagePackFormatter(typeof(CachingAudioFrameFormatter))]
public partial class AudioFrame : MediaFrame
{
    [DataMember(Order = 4), MemoryPackOrder(4)]
    public override TimeSpan Offset { get; init; }

    public override TimeSpan Duration { get; init; } = Constants.Audio.OpusFrameDuration;
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
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    internal ReadOnlyMemory<byte> SerializedData { get; set; }
}
