using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal static class ChatSliceExt
{
    // TODO: Check how feasible is it to move this logic into ingest pipelines.
    // Note: Intent to have a separate method.
    // This is a temporary method till we implement ingest pipeline
    // that would make document uri unique and add int into a separate
    // lookup index. This pipeline should get document id from that index
    // as well. This will eliminate this method entirely.
    // Note: OpenSearch _id key has a limit of 512 bytes string.
    // Note: Moving this into ingest pipeline would require same logic applied for deletions.
    public static string Id(this ChatSlice document)
        => document.Id;

    public static string IntoDocumentId(this ChatEntry chatEntry)
        => ChatSlice.FormatId(chatEntry.Id, default);

    public static ChatSlice IntoIndexedDocument(this ChatEntry chatEntry)
    {
        var metadata = new ChatSliceMetadata(
            // TODO: verify
            AuthorId: new PrincipalId(chatEntry.AuthorId.Id),
            ChatEntries: [new (chatEntry.Id, chatEntry.LocalId, chatEntry.Version)],
            // TODO: ensure everything is correct here.
            StartOffset: null,
            EndOffset: null,
            ReplyToEntries: [],
            Mentions: [],
            // TODO: talk: seems it's a bit too much.
            Reactions: [],
            ConversationParticipants: [],
            Attachments: [],
            // TODO:
            IsPublic: true,
            Language: null,
            // TODO:
            Timestamp: chatEntry.BeginsAt
        );
        return new ChatSlice(metadata, chatEntry.Content);
    }
}
