using ActualChat.Blobs;

namespace ActualChat.Media;

[DataContract]
public abstract class MediaFrame
{
    [DataMember(Order = 0)]
    public TimeSpan Offset { get; init; }
    [DataMember(Order = 1)]
    public TimeSpan Duration { get; init; }
    [DataMember(Order = 2)]
    public byte[] Data { get; init; } = Array.Empty<byte>();

    public abstract bool IsKeyFrame { get; }

    public virtual BlobPart ToBlobPart(int index = 0)
        => new(index, Data);
}
