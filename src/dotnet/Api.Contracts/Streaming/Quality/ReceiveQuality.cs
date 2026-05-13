namespace ActualChat.Streaming;

/// <summary>
/// Per-stream client request for the SVC layer cap the server should forward.
/// <see cref="LayerId"/> is the inclusive max kept spatial layer id.
/// <see cref="TemporalLayerId"/> is the requested temporal drop depth:
/// 0 keeps all frames, 1 drops the highest temporal enhancement layer, and so on.
/// <see cref="Lowest"/> is the lightweight equivalent of pausing the stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ReceiveQuality
{
    public static readonly ReceiveQuality Lowest = new(0, int.MaxValue);
    public static readonly ReceiveQuality Default = new(1, 0);

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public int LayerId { get; init; }

    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public int TemporalLayerId { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsLowest => LayerId <= 0 && TemporalLayerId > 0;

    [SerializationConstructor]
    public ReceiveQuality(int layerId, int temporalLayerId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(layerId);
        ArgumentOutOfRangeException.ThrowIfNegative(temporalLayerId);
        LayerId = layerId;
        TemporalLayerId = temporalLayerId;
    }
}
