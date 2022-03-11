namespace ActualChat.Media;

[DataContract]
public abstract class MediaFrame
{
    [DataMember(Order = 0)]
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public abstract TimeSpan Offset { get; init; }
    public abstract TimeSpan Duration { get; }
    public abstract bool IsKeyFrame { get; }
}
