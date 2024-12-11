using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

internal static class IndexedDocumentsExt
{
    public static Task UpdateEntries(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedEntry> updated,
        IReadOnlyCollection<TextEntryId> deleted,
        CancellationToken cancellationToken = default)
        => indexedDocuments.Update(x => x.EntryIndexName, updated, deleted, cancellationToken);

    public static Task UpdateChats(
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

    public static Task UpdateGroupContacts(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedGroupContact> updatedDocuments,
        IReadOnlyCollection<ChatId> deleted,
        CancellationToken cancellationToken = default)
        => indexedDocuments.Update(x => x.GroupIndexName, updatedDocuments, deleted, cancellationToken);
}
