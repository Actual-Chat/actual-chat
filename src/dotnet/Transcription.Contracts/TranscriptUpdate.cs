namespace ActualChat.Transcription;

[DataContract]
public sealed record TranscriptUpdate
{
    [DataMember(Order = 0)]
    public Transcript? UpdatedPart { get; init; }

    public TranscriptUpdate() { }
    public TranscriptUpdate(Transcript? updatedPart)
        => UpdatedPart = updatedPart;


    // This record relies on referential equality
    public bool Equals(TranscriptUpdate? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}
