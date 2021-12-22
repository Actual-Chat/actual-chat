namespace ActualChat.Audio;

[DataContract]
public class AudioMetadata
{
    [DataMember(Order = 0)]
    public IReadOnlyList<AudioMetadataEntry> Entries { get; init; }

    public AudioMetadata()
        => Entries = new List<AudioMetadataEntry>();
}
