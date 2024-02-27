
namespace ActualChat.MLSearch;

internal class IndexedDocument
{
    public string Uri;
    public string Text;
}

internal class DocumentMetadata
{
    public UserId Author { get; set;}
    public ChatId ChatId { get; }
    public PlaceId? PlaceId { get; }
    public ChatEntryId? ReplyTo { get; set; }
    public bool IsPublic { get; set; }
    public string Language { get; set; }

    public DateTime TimeStamp { get; set; }

    // Ordered list of all involved chat entries.
    public ImmutableArray<ChatEntryId> ChatEntries { get; set; }

    // Offset from the beginning of the text of 1st entry in the chat entry list.
    // This is the place where document starts.
    public int? StartOffset { get; set; }
    // Offset from the beginning of the last enrty in the chat entry list.
    // That is the plase where document ends.
    public int? EndOffset { get; set; }

    // A list of users explicitly mentioned in the document text.
    public ImmutableArray<UserId> Mentions { get; set; }
    // A list of users who reacted to at least one of the source messages.
    public ImmutableArray<UserId> Reactions { get; set; }
    // A list of users who are identifyed as a participants of a conversation
    // happenig at the document creation time.
    public ImmutableArray<UserId> ConversationParticipants { get; set; }

    public ImmutableArray<DocumentAttachment> Attachments { get; set; }
}

[StructLayout(LayoutKind.Auto)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
internal readonly struct DocumentAttachment(MediaId id, string summary)
{
    public MediaId Id { get; } = id;
    public string Summary { get; } = summary;
}
