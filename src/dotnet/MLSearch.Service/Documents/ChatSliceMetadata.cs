
namespace ActualChat.MLSearch.Documents;

[StructLayout(LayoutKind.Auto)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
public readonly record struct ChatSliceMetadata(
    // A list of users who authored messages included in document content.
    ImmutableArray<PrincipalId> Authors,
    // The ordered list of all involved chat entry ids.
    ImmutableArray<ChatSliceEntry> ChatEntries,
    // Offset from the beginning of the text of the 1st entry in the chat entry list.
    // This is the place where the document starts.
    int? StartOffset,
    // Offset from the beginning of the last entry in the chat entry list.
    // That is the place where the document ends.
    int? EndOffset,
    // Ids of entries replied by document entries.
    ImmutableArray<TextEntryId> ReplyToEntries,
    // A list of users explicitly mentioned in the document text.
    ImmutableArray<PrincipalId> Mentions,
    // A list of users who reacted to at least one of the source messages.
    ImmutableArray<PrincipalId> Reactions,
    // Attachments to document's source messages
    ImmutableArray<ChatSliceAttachment> Attachments,
    string? Language,
    DateTime ContentTimestamp
)
{
    public ChatId? ChatId => ChatEntries.IsDefaultOrEmpty
        ? null
        : ChatEntries[0].Id.ChatId;

    public PlaceId? PlaceId => ChatId is PlaceChatId placeChatId
        ? placeChatId.PlaceId
        : null;
}
