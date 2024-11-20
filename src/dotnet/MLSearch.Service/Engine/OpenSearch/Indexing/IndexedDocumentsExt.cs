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
        // NOTE: IndexedChat and IndexedEntry are stored in the same index
        => indexedDocuments.Update<IndexedChat, ChatId>(x => x.EntryIndexName, updatedDocuments, [], cancellationToken);

    public static Task UpdatePlaceContacts(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedPlaceContact> updatedDocuments,
        IReadOnlyCollection<PlaceId> deleted,
        CancellationToken cancellationToken = default)
        => indexedDocuments.Update(x => x.PlaceIndexName, updatedDocuments, deleted, cancellationToken);
}
