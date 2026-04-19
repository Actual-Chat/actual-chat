namespace ActualChat.Audio;

/// <summary>
/// Represents a single frame of audio data.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class AudioFrame : MediaFrame
{
    [DataMember(Order = 4), MemoryPackOrder(4)]
    public override TimeSpan Offset { get; init; }

    public override TimeSpan Duration { get; init; } = Constants.Audio.OpusFrameDuration;
    public override bool IsKeyFrame { get; init; } = true;

    /// <summary>
    /// Cached serialized bytes for zero-copy forwarding and serialize-once fan-out.
    /// Set once during deserialization (<see cref="CachingAudioFrameFormatter"/>) or first
    /// RPC serialization. Subsequent consumers reuse cached bytes. Do not mutate after
    /// initial assignment. Backed by a plain GC-managed <c>byte[]</c>; <see cref="MediaFrame.Data"/>
    /// is a slice into the same array, so references live as long as any consumer holds
    /// this frame — no Dispose dance, no use-after-free race.
    /// </summary>
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    internal ReadOnlyMemory<byte> SerializedData { get; set; }
}
