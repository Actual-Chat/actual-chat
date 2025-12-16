using ActualChat.Contacts;
using ActualChat.Flows;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Module;
using ActualChat.Queues;
using ActualChat.Search;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class PlaceContactIndexingFlow : BatchedIndexingFlow<Contact, ContactId>, IMasterFlow
{
    private IContactsBackend ContactsBackend => field ??= Services.GetRequiredService<IContactsBackend>();
    private IndexedDocuments IndexedDocuments => field ??= Services.GetRequiredService<IndexedDocuments>();
    private MLSearchSettings Settings => field ??= Services.GetRequiredService<MLSearchSettings>();
    private Task WhenReady => field ??= Services.GetRequiredService<OpenSearchConfigurator>().WhenReady;

    protected override async Task<IReadOnlyList<Contact>> GetBatch(
        IndexingFlowCursor<ContactId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = Hub.SystemNow.ToVersion(-Settings.ChangedEntityIndexingDelay);
        cursor ??= new(null, 0);
        var query = new ChangedContactsQuery {
            LastId = cursor.LastUpdatedId,
            Limit = BatchSize,
            MinVersion = cursor.LastUpdatedVersion,
            MaxVersion = maxVersion,
        };
        var batch = await ContactsBackend.ListChangedPlaceContacts(query, cancellationToken).ConfigureAwait(false);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<Contact> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);

        var indexedUsers = await batch.Select(x => x.OwnerId)
            .Distinct()
            .Select(ToIndexedUser)
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        var updated = indexedUsers.SkipNullItems().ToArray();
        await IndexedDocuments
            .UpsertPartially<IndexedUser, IIndexedUserUpsertForPlacesOnly, UserId>(x => x.UserIndexName,
                updated,
                cancellationToken)
            .ConfigureAwait(false);
        return;

        async Task<IndexedUser?> ToIndexedUser(UserId userId)
        {
            var placeIds = await ContactsBackend.ListPlaceIds(userId, cancellationToken).ConfigureAwait(false);
            return IndexedUser.ForPartialPlacesUpsert(userId, placeIds);
        }
    }

    protected override async ValueTask TailReached(bool hasProcessedAnyItems, CancellationToken cancellationToken)
    {
        await base.TailReached(hasProcessedAnyItems, cancellationToken).ConfigureAwait(false);
        if (hasProcessedAnyItems) {
            Console.Log("Requesting user index refresh");
            await Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshUsers: true), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
