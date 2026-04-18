using ActualChat.Audio;

namespace ActualChat.Media;

/// <summary>
/// Base class for media data frames (audio, video).
/// </summary>
[DataContract, MemoryPackable, MessagePackObject(true)]
[MemoryPackUnion(0, typeof(AudioFrame))]
[MemoryPackUnion(1, typeof(Video.VideoFrame))]
[Union(0, typeof(AudioFrame))]
[Union(1, typeof(Video.VideoFrame))]
public abstract partial class MediaFrame : IDisposable
{
    // ReadOnlyMemory<byte> for zero-copy slicing and reduced GC pressure.
    // Setter (rather than init) allows CachingVideoFrameFormatter.Deserialize to re-point
    // Data at a slice of SerializedData after caching, eliminating duplicate storage.
    [DataMember(Order = 0), MemoryPackOrder(0), Key("data")]
    public ReadOnlyMemory<byte> Data { get; set; }

    [DataMember(Order = 1), MemoryPackOrder(1), Key("offset")]
    public abstract TimeSpan Offset { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key("duration")]
    public abstract TimeSpan Duration { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key("isKeyFrame")]
    public abstract bool IsKeyFrame { get; init; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }
}
