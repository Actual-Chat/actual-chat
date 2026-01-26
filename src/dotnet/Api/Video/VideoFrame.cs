using ActualChat.Media;
using MemoryPack;

namespace ActualChat.Video;

[DataContract, MemoryPackable]
public partial class VideoFrame(bool isKeyFrame) : MediaFrame
{
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public override TimeSpan Offset { get; init; }

    [DataMember(Order = 2), MemoryPackOrder(2)]
    public override TimeSpan Duration { get; init; }

    [DataMember(Order = 3), MemoryPackOrder(3)]
    public override bool IsKeyFrame { get; } = isKeyFrame;

    [DataMember(Order = 4), MemoryPackOrder(4)]
    public int Width { get; init; }

    [DataMember(Order = 5), MemoryPackOrder(5)]
    public int Height { get; init; }
}
