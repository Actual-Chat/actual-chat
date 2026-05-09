namespace ActualChat.Video;

/// <summary>
/// One captured source moment, carried as a simulcast bundle of 1..N
/// per-layer <see cref="VideoFrame"/>s. Ordered bottom-first
/// (Frames[0] = lowest layer; last entry = top layer). All frames in a
/// bundle share capture time, keyframe policy, source dims and codec —
/// only Data, Width/Height, Description and LayerId differ.
/// </summary>
[DataContract, MemoryPackable, MessagePackObject]
public partial class VideoFrameBundle
{
    [MemoryPackConstructor]
    public VideoFrameBundle() { }

    public VideoFrameBundle(VideoFrame[] frames)
        => Frames = frames;

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public VideoFrame[] Frames { get; init; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int LayerCount => Frames.Length;
}
