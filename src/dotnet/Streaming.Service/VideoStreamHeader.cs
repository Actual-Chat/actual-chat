using MemoryPack;

namespace ActualChat.Streaming;

[DataContract, MemoryPackable]
public partial class VideoStreamHeader(
    Moment beginsAt,
    string codec,
    int width,
    int height,
    StreamId? audioStreamId)
{
    [DataMember, MemoryPackOrder(0)]
    public Moment BeginsAt { get; init; } = beginsAt;

    [DataMember, MemoryPackOrder(1)]
    public string Codec { get; init; } = codec;

    [DataMember, MemoryPackOrder(2)]
    public int Width { get; init; } = width;

    [DataMember, MemoryPackOrder(3)]
    public int Height { get; init; } = height;

    [DataMember, MemoryPackOrder(4)]
    public StreamId? AudioStreamId { get; init; } = audioStreamId;

    public byte[] Serialize()
        => MemoryPackSerializer.Serialize(this);
}
