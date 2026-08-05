namespace ActualChat.Transcription;

// Text is rendered with MarkupFormatter.ReadableUnstyled, so mentions are "@Name", not "@a:<chatId>:<localId>"

/// <summary>
/// One preceding chat message passed to a transcriber as recognition context.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public readonly partial record struct TranscriptionContextEntry
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public long AuthorLocalId { get; init; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public string Text { get; init; }

    [MemoryPackConstructor]
    public TranscriptionContextEntry(long authorLocalId, string text)
    {
        AuthorLocalId = authorLocalId;
        Text = text;
    }
}
