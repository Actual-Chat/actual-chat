using ActualChat.Audio;
using MemoryPack;
using MessagePack;

namespace ActualChat.Media;

/// <summary>
/// Base class for media data frames (audio, video).
/// </summary>
[DataContract, MemoryPackable, MessagePackObject(true)]
[MemoryPackUnion(0, typeof(AudioFrame))]
[MemoryPackUnion(1, typeof(Video.VideoFrame))]
[Union(0, typeof(AudioFrame))]
[Union(1, typeof(Video.VideoFrame))]
public abstract partial class MediaFrame
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key("data")]
    public byte[] Data { get; init; } = [];

    // Note: These are marked IgnoreMember for MessagePack because they're overridden
    // in derived classes with their own [Key] attributes
    [DataMember(Order = 1), MemoryPackOrder(1), IgnoreMember]
    public abstract TimeSpan Offset { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), IgnoreMember]
    public abstract TimeSpan Duration { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3), IgnoreMember]
    public abstract bool IsKeyFrame { get; init; }
}
