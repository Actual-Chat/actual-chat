namespace ActualChat.Audio;

[DataContract]
public class AudioMetadataEntry
{
    [DataMember(Order = 0)]
    public TimeSpan Offset { get; init; }
    [DataMember(Order = 1)]
    public Moment? RecordedAt { get; init; }
    [DataMember(Order = 2)]
    public float? VoiceProbability { get; init; }
}
