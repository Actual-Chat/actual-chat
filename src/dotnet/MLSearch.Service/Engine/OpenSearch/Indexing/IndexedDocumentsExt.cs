using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

internal static class IndexedDocumentsExt
{
    public static Task Update(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedEntry> updated,
        IReadOnlyCollection<TextEntryId> deleted,
        CancellationToken cancellationToken = default)
        => indexedDocuments.Update(x => x.EntryIndexName, updated, deleted, cancellationToken);

    public static Task Update(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedChat> updatedDocuments,
        CancellationToken cancellationToken = default)
        => indexedDocuments.Update<IndexedChat, ChatId>(x => x.EntryIndexName, updatedDocuments, [], cancellationToken);
}
