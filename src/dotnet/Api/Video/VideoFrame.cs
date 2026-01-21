using ActualChat.Media;
using MemoryPack;

namespace ActualChat.Video;

[DataContract, MemoryPackable]
public partial class VideoFrame(bool isKeyFrame) : MediaFrame
{
    [DataMember, MemoryPackOrder(4)]
    public override TimeSpan Offset { get; init; }

    [DataMember, MemoryPackOrder(5)]
    public override TimeSpan Duration { get; init; }

    [DataMember, MemoryPackOrder(6)]
    public override bool IsKeyFrame { get; } = isKeyFrame;

    [DataMember, MemoryPackOrder(7)]
    public string Codec { get; init; } = "avc1"; // H.264 by default

    [DataMember, MemoryPackOrder(8)]
    public int Width { get; init; }

    [DataMember, MemoryPackOrder(9)]
    public int Height { get; init; }

    [DataMember, MemoryPackOrder(10)]
    public int SequenceNumber { get; init; }
}
