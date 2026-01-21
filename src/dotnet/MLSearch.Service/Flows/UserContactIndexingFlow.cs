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
public partial class UserContactIndexingFlow : BatchedIndexingFlow<Contact, ContactId>, IMasterFlow
{
    private IndexedDocuments IndexedDocuments => field ??= Services.GetRequiredService<IndexedDocuments>();
    private IContactsBackend ContactsBackend => field ??= Services.GetRequiredService<IContactsBackend>();
    private MLSearchSettings Settings => field ??= Services.GetRequiredService<MLSearchSettings>();
    private Task WhenReady => field ??= Services.GetRequiredService<OpenSearchConfigurator>().WhenReady;

    protected override async Task<IReadOnlyList<Contact>> GetBatch(
        IndexingFlowCursor<ContactId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = ResumedAt.ToVersion(-Settings.ChangedEntityIndexingDelay);
        cursor ??= new(null, 0);
        var query = new ChangedContactsQuery {
            LastId = cursor.LastUpdatedId,
            Limit = BatchSize,
            MinVersion = cursor.LastUpdatedVersion,
            MaxVersion = maxVersion,
        };
        return await ContactsBackend.ListChangedPeerContacts(query,
            cancellationToken
            ).ConfigureAwait(false);
    }

    protected override async Task ProcessBatch(IReadOnlyList<Contact> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);

        // TODO(FC): in one request
        var indexedUsers = batch
            .Select(c => c.UserId)
            .SkipNullItems()
            .Distinct()
            .Select(userId => new IndexedUser(userId))
            .ToArray();
        await IndexedDocuments
            .UpsertPartially<IndexedUser, IIndexedUserMinimalUpsert, UserId>(x => x.UserIndexName,
                indexedUsers,
                cancellationToken)
            .ConfigureAwait(false);
        var indexedUserContacts = batch.Select(x => x.ToIndexedUserContact()).ToArray();
        await IndexedDocuments.SaveUserContacts(indexedUserContacts, [], cancellationToken).ConfigureAwait(false);
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
