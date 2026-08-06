namespace ActualChat.Transcription;

// Text is rendered with MarkupFormatter.ReadableUnstyled, so mentions are "@Name", not "@a:<chatId>:<localId>"

/// <summary>
/// One preceding chat message passed to a transcriber as recognition context.
/// </summary>
[DataContract, MessagePackObject]
public readonly partial record struct TranscriptionContextEntry
{
    [DataMember(Order = 0), Key(0)]
    public long AuthorLocalId { get; init; }
    [DataMember(Order = 1), Key(1)]
    public string Text { get; init; }

    public TranscriptionContextEntry(long authorLocalId, string text)
    {
        AuthorLocalId = authorLocalId;
        Text = text;
    }
}
