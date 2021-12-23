namespace ActualChat.Media;

[DataContract]
public class MediaFrameMetadata
{
    [DataMember(Order = 0)]
    public Moment? RecordedAt { get; init; }
    [DataMember(Order = 1)]
    public float? VoiceProbability { get; init; }
}
