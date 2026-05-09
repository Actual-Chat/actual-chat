namespace ActualChat.Video;

[DataContract, MemoryPackable, MessagePackObject]
[MessagePackFormatter(typeof(CachingVideoFrameFormatter))]
public sealed partial class VideoFrame : MediaFrame
{
    // Parameterless constructor for MessagePack and MemoryPack deserialization
    [MemoryPackConstructor]
    public VideoFrame() { }

    // Constructor for creating frames programmatically
    public VideoFrame(bool isKeyFrame)
        // ReSharper disable once VirtualMemberCallInConstructor
        => IsKeyFrame = isKeyFrame;

    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public override TimeSpan Offset { get; init; }
    /// <summary>
    /// Increments every time the sender's monotonic capture clock is resynced
    /// (sleep/wake, NTP step). The receiver uses an epoch change as the
    /// trigger to reset its decode-side anchors and re-bootstrap pacing.
    /// Senders that don't track this leave it at 0; receivers treat 0 as
    /// "no epoch information" (no-op).
    /// </summary>
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public int OffsetEpoch { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public override TimeSpan Duration { get; init; }
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public override bool IsKeyFrame { get; init; }
    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public int Width { get; init; }
    [DataMember(Order = 6), MemoryPackOrder(6), Key(6)]
    public int Height { get; init; }

    /// <summary>
    /// Codec-specific data (SPS/PPS for H.264). Only present on keyframes.
    /// ReadOnlyMemory&lt;byte&gt; for zero-copy slicing and reduced GC pressure.
    /// </summary>
    [DataMember(Order = 7), MemoryPackOrder(7), Key(7)]
    public ReadOnlyMemory<byte> Description { get; init; }

    /// <summary>
    /// Codec identifier (e.g., "avc1" for H.264). Only present on keyframes.
    /// </summary>
    [DataMember(Order = 8), MemoryPackOrder(8), Key(8)]
    public string? Codec { get; init; }

    /// <summary>
    /// SVC layer ID. 0 = base (lowest-res) layer, 1+ = higher-res layers.
    /// Always 0 on single-encoder (P2P) streams.
    /// </summary>
    [DataMember(Order = 9), MemoryPackOrder(9), Key(9)]
    public byte LayerId { get; init; }
    [DataMember(Order = 10), MemoryPackOrder(10), Key(10)]
    public byte MaxLayerId { get; init; }

    /// <summary>
    /// SVC temporal layer ID. 0 = base layer, 1+ = enhancement layers.
    /// </summary>
    [DataMember(Order = 11), MemoryPackOrder(11), Key(11)]
    public byte TemporalLayerId { get; init; }

    /// <summary>
    /// Native capture source dimensions (pre-downscale). Sent on keyframes only —
    /// lets the server track live source resolution changes (e.g. a screencast
    /// window resized from 1280x768 to full-screen 4K) and unlock the matching
    /// quality-preset ceiling. Zero when the sender doesn't populate them
    /// (legacy peers, non-keyframe deltas).
    /// </summary>
    [DataMember(Order = 12), MemoryPackOrder(12), Key(12)]
    public int SourceWidth { get; init; }
    [DataMember(Order = 13), MemoryPackOrder(13), Key(13)]
    public int SourceHeight { get; init; }
    [DataMember(Order = 14), MemoryPackOrder(14), Key(14)]
    public int MaxLayerWidth { get; init; }
    [DataMember(Order = 15), MemoryPackOrder(15), Key(15)]
    public int MaxLayerHeight { get; init; }

    // NB: The properties below this line aren't serialized!

    /// <summary>
    /// Monotonically increasing keyframe sequence number. Assigned server-side in ProcessFrames.
    /// Incremented on each keyframe; non-keyframes inherit the current value.
    /// Used for gap detection when frames are dropped by bounded replay channels.
    /// </summary>
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long KeyFrameNumber { get; set; }

    /// <summary>
    /// Cached serialized bytes for zero-copy forwarding and serialize-once fan-out.
    /// Populated at ingress by <see cref="CachingVideoFrameFormatter"/>.<c>Deserialize</c>
    /// (single-writer: the RPC read loop of the producer's peer). Fan-out consumers read
    /// only — the memoizer's <c>TrySetResult</c> establishes the happens-before edge that
    /// makes the write visible. Do not add writers outside the ingress path.
    /// Backed by a plain GC-managed <c>byte[]</c>; <see cref="Data"/> is a slice into the
    /// same array, so the array lives as long as any consumer holds this frame.
    /// </summary>
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ReadOnlyMemory<byte> SerializedData { get; set; }
}
