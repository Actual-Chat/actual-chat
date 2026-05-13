namespace ActualChat.Streaming;

/// <summary>
/// Per-stream client request for the SVC layer cap the server should forward.
/// <see cref="LayerId"/> is the inclusive max kept spatial layer id;
/// <see cref="TemporalLayerId"/> is the first temporal layer id we'd drop
/// (frames with <c>frame.TemporalLayerId >= TemporalLayerId</c> are dropped),
/// with <see cref="int.MaxValue"/> meaning "no temporal cap".
/// <see cref="Lowest"/> is the lightweight equivalent of pausing the stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ReceiveQuality
{
    public static readonly ReceiveQuality Lowest = new(0, 1);
    public static readonly ReceiveQuality Default = new(1, int.MaxValue);

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public int LayerId { get; init; }

    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public int TemporalLayerId { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsLowest => LayerId <= 0 && TemporalLayerId >= 1;

    [SerializationConstructor]
    public ReceiveQuality(int layerId, int temporalLayerId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(layerId);
        ArgumentOutOfRangeException.ThrowIfLessThan(temporalLayerId, 1);
        LayerId = layerId;
        TemporalLayerId = temporalLayerId;
    }
}
