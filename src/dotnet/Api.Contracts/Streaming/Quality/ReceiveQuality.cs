namespace ActualChat.Streaming;

/// <summary>
/// Per-stream client request for the maximum SVC layers the server should forward.
/// <see cref="Lowest"/> means "send only the base spatial layer", which is the
/// lightweight equivalent of pausing the stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ReceiveQuality(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int MaxSpatialLayer,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] int MaxTemporalLayer)
{
    public static readonly ReceiveQuality Lowest = new(0, 0);
    public static readonly ReceiveQuality Default = new(2, int.MaxValue);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsLowest => MaxSpatialLayer <= 0 && MaxTemporalLayer <= 0;
}
