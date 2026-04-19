namespace ActualChat.Video;

[DataContract, MemoryPackable, MessagePackObject(true)]
public partial class VideoFrame : MediaFrame
{
    // Parameterless constructor for MessagePack and MemoryPack deserialization
    [MemoryPackConstructor]
    public VideoFrame() { }

    // Constructor for creating frames programmatically
    public VideoFrame(bool isKeyFrame)
        => IsKeyFrame = isKeyFrame;

    [DataMember(Order = 1), MemoryPackOrder(1)]
    public override TimeSpan Offset { get; init; }

    [DataMember(Order = 2), MemoryPackOrder(2)]
    public override TimeSpan Duration { get; init; }

    [DataMember(Order = 3), MemoryPackOrder(3)]
    public override bool IsKeyFrame { get; init; }

    [DataMember(Order = 4), MemoryPackOrder(4)]
    public int Width { get; init; }

    [DataMember(Order = 5), MemoryPackOrder(5)]
    public int Height { get; init; }

    /// <summary>
    /// Codec-specific data (SPS/PPS for H.264). Only present on keyframes.
    /// ReadOnlyMemory&lt;byte&gt; for zero-copy slicing and reduced GC pressure.
    /// </summary>
    [DataMember(Order = 6), MemoryPackOrder(6)]
    public ReadOnlyMemory<byte> Description { get; init; }

    /// <summary>
    /// Codec identifier (e.g., "avc1" for H.264). Only present on keyframes.
    /// </summary>
    [DataMember(Order = 7), MemoryPackOrder(7)]
    public string? Codec { get; init; }

    /// <summary>
    /// SVC temporal layer ID. 0 = base layer, 1+ = enhancement layers.
    /// </summary>
    [DataMember(Order = 8), MemoryPackOrder(8)]
    public int TemporalLayerId { get; init; }

    /// <summary>
    /// Monotonically increasing keyframe sequence number. Assigned server-side in ProcessFrames.
    /// Incremented on each keyframe; non-keyframes inherit the current value.
    /// Used for gap detection when frames are dropped by bounded replay channels.
    /// </summary>
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long KeyFrameNumber { get; set; }

    /// <summary>
    /// Cached serialized bytes for zero-copy forwarding and serialize-once fan-out.
    /// Set once during deserialization (<see cref="CachingVideoFrameFormatter"/>) or first
    /// RPC serialization. Subsequent consumers reuse cached bytes. Do not mutate after
    /// initial assignment. Backed by a plain GC-managed <c>byte[]</c>; <see cref="Data"/>
    /// is a slice into the same array, so references live as long as any consumer holds
    /// this frame — no Dispose dance, no use-after-free race.
    /// </summary>
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    internal ReadOnlyMemory<byte> SerializedData { get; set; }
}
