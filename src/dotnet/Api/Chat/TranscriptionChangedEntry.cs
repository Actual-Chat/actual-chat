namespace ActualChat.Chat;

/// <summary>
/// System entry recording who turned a call's transcription on or off.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record TranscriptionChangedEntry(ChatEntryId Id, long Version = 0) : SystemEntry(Id, Version)
{
    [DataMember, Key(20)] public AuthorId? TargetAuthorId { get; init; }
    [DataMember, Key(21)] public string TargetAuthorName { get; init; } = "";
    [DataMember, Key(22)] public bool IsTranscriptionOn { get; init; }

    public TranscriptionChangedEntry() : this(null!, 0)
    { }
}
