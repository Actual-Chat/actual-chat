namespace ActualChat.Audio;

[DataContract]
public class AudioMetadata
{
    [DataMember(Order = 0)]
    public ImmutableArray<AudioMetadataEntry> Entries { get; init; } = ImmutableArray<AudioMetadataEntry>.Empty;
}
