namespace ActualChat.Video;

[DataContract, MemoryPackable, MessagePackObject]
[MessagePackFormatter(typeof(CachingVideoFrameFormatter))]
public sealed partial record VideoFrame : MediaFrame
{
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public override TimeSpan Offset { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public override TimeSpan Duration { get; init; }
    // Key/Order 3 is reserved for the obsolete IsKeyFrame wire field.
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public int OffsetEpoch { get; init; }

    /// <summary>
    /// Sender-assigned source-moment counter. Drops at any pipeline stage
    /// surface as gaps in <c>Index</c> for the same-layer stream. 0 if the
    /// upstream peer doesn't populate it (legacy clients).
    /// </summary>
    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public int Index { get; init; }
    /// <summary>
    /// Per-layer pointer to the keyframe this frame belongs to: the value is the
    /// keyframe's own <see cref="Index"/>. A frame is a keyframe iff
    /// <see cref="Index"/> == <see cref="KeyFrameIndex"/>. Sender-assigned;
    /// passed through the server unchanged.
    /// </summary>
    [DataMember(Order = 6), MemoryPackOrder(6), Key(6)]
    public int KeyFrameIndex { get; init; }

    [DataMember(Order = 7), MemoryPackOrder(7), Key(7)]
    public int Width { get; init; }
    [DataMember(Order = 8), MemoryPackOrder(8), Key(8)]
    public int Height { get; init; }
    [DataMember(Order = 9), MemoryPackOrder(9), Key(9)]
    public byte Rotation { get; init; }

    /// <summary>
    /// SVC layer ID. 0 = base (lowest-res) layer, 1+ = higher-res layers.
    /// Always 0 on single-encoder (P2P) streams.
    /// </summary>
    [DataMember(Order = 10), MemoryPackOrder(10), Key(10)]
    public byte LayerId { get; init; }
    [DataMember(Order = 11), MemoryPackOrder(11), Key(11)]
    public byte LayerCount { get; init; } = 1;
    [DataMember(Order = 12), MemoryPackOrder(12), Key(12)]
    public int MaxLayerWidth { get; init; }
    [DataMember(Order = 13), MemoryPackOrder(13), Key(13)]
    public int MaxLayerHeight { get; init; }

    /// <summary>
    /// SVC temporal layer ID. 0 = base layer, 1+ = enhancement layers.
    /// </summary>
    [DataMember(Order = 14), MemoryPackOrder(14), Key(14)]
    public byte TemporalLayerId { get; init; }
    [DataMember(Order = 15), MemoryPackOrder(15), Key(15)]
    public byte TemporalLayerCount { get; init; } = 1;

    /// <summary>
    /// Codec identifier (e.g., "avc1" for H.264). Only present on keyframes.
    /// </summary>
    [DataMember(Order = 16), MemoryPackOrder(16), Key(16)]
    public string? Codec { get; init; }
    /// <summary>
    /// Codec-specific data (SPS/PPS for H.264). Only present on keyframes.
    /// ReadOnlyMemory&lt;byte&gt; for zero-copy slicing and reduced GC pressure.
    /// </summary>
    [DataMember(Order = 17), MemoryPackOrder(17), Key(17)]
    public ReadOnlyMemory<byte> Description { get; init; }

    /// <summary>
    /// End-to-end drop attribution. Each byte is a <see cref="FrameDropStage"/>
    /// enum value identifying one dropped predecessor frame. Empty on the very
    /// first frame; subsequent frames append entries when an operator's local
    /// detector observes a gap larger than the trace already covers.
    /// </summary>
    [DataMember(Order = 18), MemoryPackOrder(18), Key(18)]
    public ReadOnlyMemory<byte> DropTrace { get; init; }

    // Stamped server-side in VideoStreamingBackend.ProcessFrames at ingest
    // (DateTime.UtcNow.Ticks); 0 on the sender->server leg. Receiver derives
    // downlink latency = (receiver.now - ServerArrivedAtTicks/10000) - minSkew.
    // Mutable post-construction like SerializedData: server invalidates the
    // cache after stamping so fan-out rebuilds the bytes once.
    [DataMember(Order = 19), MemoryPackOrder(19), Key(19)]
    public long ServerArrivedAtTicks { get; set; }

    // NB: The properties below this line aren't serialized!

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsKeyFrame => KeyFrameIndex == Index;

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

    public override string ToString()
    {
        var k = IsKeyFrame ? "K" : "";
        return $"VideoFrame(#{Index}{k}: {Duration.ToShortString()} @ {Offset.ToShortString()}, "
            + $"{Width}x{Height}, L{LayerId}/{LayerCount}({MaxLayerWidth}x{MaxLayerHeight}), "
            + $"T{TemporalLayerId}/{TemporalLayerCount})";
    }

    // This record relies on reference equality
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public bool Equals(VideoFrame? other) => ReferenceEquals(this, other);
}
