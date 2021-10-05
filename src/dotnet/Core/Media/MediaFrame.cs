namespace ActualChat.Media;

[DataContract]
public abstract class MediaFrame
{
    [DataMember(Order = 0)]
    public Moment Timestamp { get; init; }
    [DataMember(Order = 1)]
    public ReadOnlyMemory<byte> Data { get; init; }

    public abstract bool IsKeyFrame { get; }
}
