namespace ActualChat.Streaming;

/// <summary>
/// Per-stream client request for the maximum SVC layers the server should forward.
/// <see cref="Lowest"/> means "send only the base layer", which is the
/// lightweight equivalent of pausing the stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ReceiveQuality
{
    public static readonly ReceiveQuality Lowest = new(1, 1);
    public static readonly ReceiveQuality Default = new(2, int.MaxValue);

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public int LayerCount { get; init; }

    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public int TemporalLayerCount { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsLowest => LayerCount <= 1 && TemporalLayerCount <= 1;

    [SerializationConstructor]
    public ReceiveQuality(int layerCount, int temporalLayerCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(layerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(temporalLayerCount, 1);
        LayerCount = layerCount;
        TemporalLayerCount = temporalLayerCount;
    }
}
