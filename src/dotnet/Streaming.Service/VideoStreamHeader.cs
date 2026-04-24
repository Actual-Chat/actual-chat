namespace ActualChat.Streaming;

[DataContract, MemoryPackable, MessagePackObject]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
public partial class VideoStreamHeader(
    Moment beginsAt,
    string codec,
    int width,
    int height,
    StreamId? audioStreamId)
{
    [DataMember, MemoryPackOrder(0), Key(0)] public Moment BeginsAt { get; init; } = beginsAt;
    [DataMember, MemoryPackOrder(1), Key(1)] public string Codec { get; init; } = codec;
    [DataMember, MemoryPackOrder(2), Key(2)] public int Width { get; init; } = width;
    [DataMember, MemoryPackOrder(3), Key(3)] public int Height { get; init; } = height;
    [DataMember, MemoryPackOrder(4), Key(4)] public StreamId? AudioStreamId { get; init; } = audioStreamId;
}
