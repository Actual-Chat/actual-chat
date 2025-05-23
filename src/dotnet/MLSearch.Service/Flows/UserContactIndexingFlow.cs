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
public partial class UserContactIndexingFlow : BatchedIndexingFlowBase<Contact, ContactId>, IMasterFlow
{
    [field: AllowNull, MaybeNull]
    private IndexedDocuments IndexedDocuments => field ??= Host.Services.GetRequiredService<IndexedDocuments>();
    [field: AllowNull, MaybeNull]
    private IContactsBackend ContactsBackend => field ??= Host.Services.GetRequiredService<IContactsBackend>();
    [field: AllowNull, MaybeNull]
    private MLSearchSettings Settings => field ??= Host.Services.GetRequiredService<MLSearchSettings>();
    [field: AllowNull, MaybeNull]
    private Task WhenReady => field ??= Host.Services.GetRequiredService<OpenSearchConfigurator>().WhenCompleted;

    protected override int CurrentFlowSetVersion => 2;

    protected override async Task<IReadOnlyList<Contact>> GetBatch(
        IndexingFlowCursor<ContactId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = Clocks.GetMaxVersion(Settings.ChangedEntityIndexingDelay);
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

    protected override async Task<IndexingFlowTransitionKind> HandleTail(
        bool hasProcessedAnyItems,
        CancellationToken cancellationToken)
    {
        var transition = await base.HandleTail(hasProcessedAnyItems, cancellationToken).ConfigureAwait(false);
        if (hasProcessedAnyItems) {
            Log.LogInformation("`{Id}`.OnTailReached: requesting user index refresh", Id);
            await Host.Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshUsers: true), cancellationToken)
                .ConfigureAwait(false);
        }
        return transition;
    }
}
