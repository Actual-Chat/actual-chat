namespace ActualChat.Video;

/// <summary>
/// One captured source moment, carried as a simulcast bundle of 1..N
/// per-layer <see cref="VideoFrame"/>s. Ordered bottom-first
/// (Layers[0] = lowest layer; last entry = top layer). All frames in a
/// bundle share capture time, keyframe policy, source dims and codec —
/// only Data, Width/Height, Description and LayerId differ.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial class VideoFrameBundle(VideoFrame[] layers)
{
    [DataMember(Order = 0), Key(0)]
    public VideoFrame[] Layers { get; init; } = layers;

    [JsonIgnore, IgnoreDataMember, IgnoreMember]
    public int LayerCount => Layers.Length;
    [JsonIgnore, IgnoreDataMember, IgnoreMember]
    public VideoFrame TopLayer => Layers[^1];
    [JsonIgnore, IgnoreDataMember, IgnoreMember]
    public VideoFrame BottomLayer => Layers[0];

    /// <summary>
    /// True iff EVERY layer in the bundle is a keyframe. Per-layer
    /// <see cref="VideoFrame.IsKeyFrame"/> can diverge — encoders may emit a
    /// KF unilaterally on reset/recovery even when only a delta was requested.
    /// A bundle is a real keyframe only when all spatial layers agree.
    /// </summary>
    [JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsKeyFrame => Layers.Length != 0 && Layers.All(layer => layer.IsKeyFrame);
}
