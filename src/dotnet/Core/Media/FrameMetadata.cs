namespace ActualChat.Media;

[DataContract]
public class FrameMetadata
{
    [DataMember(Order = 0)]
    public long? UtcTicks { get; init; }

    [DataMember(Order = 1)]
    public float? VoiceProbability { get; init; }
}
