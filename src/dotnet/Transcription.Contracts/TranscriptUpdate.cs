namespace ActualChat.Transcription;

[DataContract]
public record TranscriptUpdate
{
    [DataMember(Order = 0)]
    public Transcript? UpdatedPart { get; init; }

    public TranscriptUpdate() { }
    public TranscriptUpdate(Transcript? updatedPart)
        => UpdatedPart = updatedPart;
}
