using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

internal static class IndexedDocumentsExt
{
    public static Task SaveEntries(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedEntry> updated,
        IReadOnlyCollection<TextEntryId> deleted,
        CancellationToken cancellationToken = default)
        => indexedDocuments.Save(x => x.EntryIndexName, updated, deleted, cancellationToken);

    public static Task SaveChats(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedChat> updatedDocuments,
        CancellationToken cancellationToken = default)
        // NOTE: IndexedChat and IndexedEntry are stored in the same index
        => indexedDocuments.Save<IndexedChat, ChatId>(x => x.EntryIndexName, updatedDocuments, [], cancellationToken);

    public static Task SavePlaces(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedPlace> updatedDocuments,
        IReadOnlyCollection<PlaceId> deleted,
        CancellationToken cancellationToken = default)
        => indexedDocuments.Save(x => x.PlaceIndexName, updatedDocuments, deleted, cancellationToken);

    public static Task SaveGroups(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedGroup> updatedDocuments,
        IReadOnlyCollection<ChatId> deleted,
        CancellationToken cancellationToken = default)
        => indexedDocuments.Save(x => x.GroupIndexName, updatedDocuments, deleted, cancellationToken);

    public static Task SaveUsers(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedUser> updatedDocuments,
        IReadOnlyCollection<UserId> deleted,
        CancellationToken cancellationToken = default)
        => indexedDocuments.Save(x => x.UserIndexName, updatedDocuments, deleted, cancellationToken);

    public static Task UpsertUsersPartially(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedUser> updatedDocuments,
        CancellationToken cancellationToken = default)
        => indexedDocuments.UpsertPartially<IndexedUser, IHasId<UserId>, UserId>(x => x.UserIndexName, updatedDocuments, cancellationToken);

    public static Task SaveUserContacts(
        this IndexedDocuments indexedDocuments,
        IReadOnlyCollection<IndexedUserContact> updatedDocuments,
        IReadOnlyCollection<ContactId> deleted,
        CancellationToken cancellationToken = default)
        => indexedDocuments.Save(x => x.UserIndexName, updatedDocuments, deleted, cancellationToken);
}
