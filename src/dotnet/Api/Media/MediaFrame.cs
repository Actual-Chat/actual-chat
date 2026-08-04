using ActualChat.Audio;

namespace ActualChat.Media;

/// <summary>
/// Base class for media data frames (audio, video).
/// </summary>
[DataContract, MessagePackObject]
[Union(0, typeof(AudioFrame))]
[Union(1, typeof(Video.VideoFrame))]
public abstract partial record MediaFrame
{
    // ReadOnlyMemory<byte> for zero-copy slicing and reduced GC pressure.
    [DataMember(Order = 0), Key(0)]
    public ReadOnlyMemory<byte> Data { get; init; }

    [DataMember(Order = 1)]
    public abstract TimeSpan Offset { get; init; }
    [DataMember(Order = 2)]
    public abstract TimeSpan Duration { get; init; }
    // Key/Order 3 is reserved for the obsolete IsKeyFrame wire field.

    // This record relies on reference equality
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public virtual bool Equals(MediaFrame? other) => ReferenceEquals(this, other);
}
