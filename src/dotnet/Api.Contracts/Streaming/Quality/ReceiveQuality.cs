namespace ActualChat.Streaming;

/// <summary>
/// Per-stream client request for the SVC layer cap the server should forward.
/// <see cref="LayerId"/> is the inclusive max kept spatial layer id;
/// <see cref="IsThumbnail"/> is the display role — true only for a true
/// thumbnail tile, false = "large or unknown" (old clients never set it).
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ReceiveQuality
{
    public static readonly ReceiveQuality Paused = new(-1);
    public static readonly ReceiveQuality Lowest = new(0);
    public static readonly ReceiveQuality Default = new(1);

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public int LayerId { get; init; }

    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public bool IsThumbnail { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsLowestOrPaused => LayerId <= 0;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsPaused => LayerId < 0;

    [SerializationConstructor]
    public ReceiveQuality(int layerId, bool isThumbnail = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(layerId, -1);
        LayerId = layerId;
        IsThumbnail = isThumbnail;
    }
}
