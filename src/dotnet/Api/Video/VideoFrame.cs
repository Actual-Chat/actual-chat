namespace ActualChat.Video;

[DataContract, MemoryPackable, MessagePackObject]
public partial class VideoFrame : MediaFrame
{
    // Parameterless constructor for MessagePack and MemoryPack deserialization
    [MemoryPackConstructor]
    public VideoFrame() { }

    // Constructor for creating frames programmatically
    public VideoFrame(bool isKeyFrame)
        => IsKeyFrame = isKeyFrame;

    [DataMember(Order = 1), MemoryPackOrder(1), Key("offset")]
    public override TimeSpan Offset { get; init; }

    [DataMember(Order = 2), MemoryPackOrder(2), Key("duration")]
    public override TimeSpan Duration { get; init; }

    [DataMember(Order = 3), MemoryPackOrder(3), Key("isKeyFrame")]
    public override bool IsKeyFrame { get; init; }

    [DataMember(Order = 4), MemoryPackOrder(4), Key("width")]
    public int Width { get; init; }

    [DataMember(Order = 5), MemoryPackOrder(5), Key("height")]
    public int Height { get; init; }

    /// <summary>
    /// Codec-specific data (SPS/PPS for H.264). Only present on keyframes.
    /// </summary>
    [DataMember(Order = 6), MemoryPackOrder(6), Key("description")]
    public byte[]? Description { get; init; }

    /// <summary>
    /// Codec identifier (e.g., "avc1" for H.264). Only present on keyframes.
    /// </summary>
    [DataMember(Order = 7), MemoryPackOrder(7), Key("codec")]
    public string? Codec { get; init; }

    /// <summary>
    /// SVC temporal layer ID. 0 = base layer, 1+ = enhancement layers.
    /// </summary>
    [DataMember(Order = 8), MemoryPackOrder(8), Key("temporalLayerId")]
    public int TemporalLayerId { get; init; }

    /// <summary>
    /// Monotonically increasing keyframe sequence number. Assigned server-side in ProcessFrames.
    /// Incremented on each keyframe; non-keyframes inherit the current value.
    /// Used for gap detection when frames are dropped by bounded replay channels.
    /// </summary>
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long KeyFrameNumber { get; set; }

    /// <summary>
    /// Cached serialized bytes for zero-copy forwarding. Set once during deserialization
    /// in StreamHub.ToVideoFrames(). Do not mutate after initial assignment.
    /// Filters must only drop frames (not mutate them), or cached bytes become stale.
    /// </summary>
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public byte[]? CachedSerializedBytes { get; set; }
}
