namespace ActualChat.Transcription;

// AltNames exists for bilingual speakers, whose name is spoken in one language and spelled in
// another - nothing populates it yet, the shape is here so adding nicknames later isn't a schema change.

/// <summary>
/// One chat participant named in the recognition context.
/// </summary>
[DataContract, MessagePackObject]
public readonly partial record struct TranscriptionAuthor
{
    [DataMember(Order = 0), Key(0)]
    public long LocalId { get; init; }
    [DataMember(Order = 1), Key(1)]
    public string Name { get; init; }
    [DataMember(Order = 2), Key(2)]
    public ApiArray<string> AltNames { get; init; }

    public TranscriptionAuthor(long localId, string name, ApiArray<string> altNames)
    {
        LocalId = localId;
        Name = name;
        AltNames = altNames;
    }

    public TranscriptionAuthor(long localId, string name) : this(localId, name, ApiArray<string>.Empty) { }
}
