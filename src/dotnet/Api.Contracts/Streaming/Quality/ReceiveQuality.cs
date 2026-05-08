namespace ActualChat.Streaming;

/// <summary>
/// Per-stream client request for the maximum SVC layers the server should forward.
/// <see cref="Lowest"/> means "send only the base layer", which is the
/// lightweight equivalent of pausing the stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ReceiveQuality(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int MaxLayerId,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] int MaxTemporalLayerId)
{
    public static readonly ReceiveQuality Lowest = new(0, 0);
    public static readonly ReceiveQuality Default = new(1, int.MaxValue);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsLowest => MaxLayerId <= 0 && MaxTemporalLayerId <= 0;
}
