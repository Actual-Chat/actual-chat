
namespace ActualChat.MLSearch.Documents;

[StructLayout(LayoutKind.Auto)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
internal readonly record struct ChatSliceMetadata(
    // An id of the author of the document.
    PrincipalId AuthorId,
    // Ordered list of all involved chat entry ids.
    ImmutableArray<ChatSliceEntry> ChatEntries,
    // Offset from the beginning of the text of 1st entry in the chat entry list.
    // This is the place where document starts.
    int? StartOffset,
    // Offset from the beginning of the last entry in the chat entry list.
    // That is the place where document ends.
    int? EndOffset,
    // Ids of entries replied by document entries.
    ImmutableArray<ChatEntryId> ReplyToEntries,
    // A list of users explicitly mentioned in the document text.
    ImmutableArray<PrincipalId> Mentions,
    // A list of users who reacted to at least one of the source messages.
    ImmutableArray<PrincipalId> Reactions,
    // A list of users who are identified as a participants of a conversation
    // happening at the document creation time.
    ImmutableArray<PrincipalId> ConversationParticipants,
    // Attachments to document's source messages
    ImmutableArray<ChatSliceAttachment> Attachments,
    bool IsPublic,
    string? Language,
    DateTime Timestamp
)
{
    public ChatId ChatId => ChatEntries.IsDefaultOrEmpty ? ChatId.None : ChatEntries[0].Id.ChatId;
    public PlaceId PlaceId => ChatId.PlaceId;
}
