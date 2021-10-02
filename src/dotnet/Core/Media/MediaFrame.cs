namespace ActualChat.Media;

[DataContract]
public abstract class MediaFrame
{
    [DataMember(Order = 0)]
    public MediaFrameKind FrameKind { get; init; }
    [DataMember(Order = 1)]
    public double Duration { get; init; }
    [DataMember(Order = 2)]
    public ReadOnlyMemory<byte> Data { get; init; }
    [DataMember(Order = 3)]
    public string Tag { get; init; } = "";
}
