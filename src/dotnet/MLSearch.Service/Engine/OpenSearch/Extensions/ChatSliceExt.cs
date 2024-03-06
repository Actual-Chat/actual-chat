using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch.Extensions;

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
    public static Id Id(this ChatSlice document)
        => new (document.Id);

    public static Id IntoDocumentId(this ChatEntry chatEntry)
        => new (ChatSlice.FormatId(chatEntry.Id, default));

    public static ChatSlice IntoIndexedDocument(this ChatEntry chatEntry)
    {
        var metadata = new ChatSliceMetadata(
            // TODO: verify
            AuthorId: new PrincipalId(chatEntry.AuthorId.Id),
            ChatEntries: [chatEntry.Id],
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
