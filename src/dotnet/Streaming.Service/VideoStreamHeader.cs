namespace ActualChat.Streaming;

[DataContract, MessagePackObject]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
public partial class VideoStreamHeader(
    Moment beginsAt,
    string codec,
    int width,
    int height,
    StreamId? audioStreamId)
{
    [DataMember, Key(0)] public Moment BeginsAt { get; init; } = beginsAt;
    [DataMember, Key(1)] public string Codec { get; init; } = codec;
    [DataMember, Key(2)] public int Width { get; init; } = width;
    [DataMember, Key(3)] public int Height { get; init; } = height;
    [DataMember, Key(4)] public StreamId? AudioStreamId { get; init; } = audioStreamId;
}
