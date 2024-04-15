using ActualChat.Audio;
using MemoryPack;

namespace ActualChat.Media;

[DataContract, MemoryPackable]
[MemoryPackUnion(0 ,typeof(AudioFrame))]
public abstract partial class MediaFrame
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public byte[] Data { get; init; } = Array.Empty<byte>();
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public abstract TimeSpan Offset { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public abstract TimeSpan Duration { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3)]
    public abstract bool IsKeyFrame { get; }
}
