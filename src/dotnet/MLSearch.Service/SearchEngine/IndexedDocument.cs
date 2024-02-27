
namespace ActualChat.MLSearch;

internal record IndexedDocument(in DocumentMetadata Metadata, string Text)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Uri => Metadata.ChatEntries.Length > 0
        ? $"{Metadata.ChatEntries[0]}:{(Metadata.StartOffset ?? 0).ToString(CultureInfo.InvariantCulture)}"
        : string.Empty;
}

[StructLayout(LayoutKind.Auto)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
internal readonly record struct DocumentMetadata(
    // An id of the author of the document.
    in PrincipalId AuthorId,
    // Ordered list of all involved chat entry ids.
    in ImmutableArray<ChatEntryId> ChatEntries,
    // Offset from the beginning of the text of 1st entry in the chat entry list.
    // This is the place where document starts.
    in int? StartOffset,
    // Offset from the beginning of the last enrty in the chat entry list.
    // That is the plase where document ends.
    in int? EndOffset,
    // Ids of entries replied by document entries.
    in ImmutableArray<ChatEntryId> ReplyToEntries,
    // A list of users explicitly mentioned in the document text.
    in ImmutableArray<PrincipalId> Mentions,
    // A list of users who reacted to at least one of the source messages.
    in ImmutableArray<PrincipalId> Reactions,
    // A list of users who are identifyed as a participants of a conversation
    // happenig at the document creation time.
    in ImmutableArray<PrincipalId> ConversationParticipants,
    // Attachments to document's source messages
    in ImmutableArray<DocumentAttachment> Attachments,
    in bool IsPublic,
    in string? Language,
    in DateTime Timestamp
)
{
    public ChatId ChatId => ChatEntries.IsDefaultOrEmpty ? ChatId.None : ChatEntries[0].ChatId;
    public PlaceId PlaceId => ChatId.PlaceId;
}

[StructLayout(LayoutKind.Auto)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
internal readonly struct DocumentAttachment(MediaId id, string summary)
{
    public MediaId Id { get; } = id;
    public string Summary { get; } = summary;
}
