using ActualChat.Audio;

namespace ActualChat.Media;

/// <summary>
/// Base class for media data frames (audio, video).
/// </summary>
[DataContract, MemoryPackable]
[MemoryPackUnion(0, typeof(AudioFrame))]
[MemoryPackUnion(1, typeof(Video.VideoFrame))]
// PolyType-native union dispatch for Nerdbank.MessagePack — tags match the MemoryPack ones above.
[DerivedTypeShape(typeof(AudioFrame), Tag = 0)]
[DerivedTypeShape(typeof(Video.VideoFrame), Tag = 1)]
public abstract partial class MediaFrame
{
    // ReadOnlyMemory<byte> for zero-copy slicing and reduced GC pressure.
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public ReadOnlyMemory<byte> Data { get; init; }

    // Abstract members are contracts — they're hidden from PolyType's shape on the base so
    // the [Key]-consistency analyzer is happy with a base that only keys the concrete `Data`.
    // Concrete derivatives (AudioFrame, VideoFrame) re-attribute their overrides with Keys
    // starting at the reserved offset (10).
    [DataMember(Order = 1), MemoryPackOrder(1), PropertyShape(Ignore = true)]
    public abstract TimeSpan Offset { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), PropertyShape(Ignore = true)]
    public abstract TimeSpan Duration { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3), PropertyShape(Ignore = true)]
    public abstract bool IsKeyFrame { get; init; }
}
