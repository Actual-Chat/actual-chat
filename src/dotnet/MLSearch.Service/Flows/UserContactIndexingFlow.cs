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

    protected override int CurrentFlowSetVersion => 1;

    protected override async Task<IReadOnlyList<Contact>> GetBatch(
        IndexingFlowCursor<ContactId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = Clocks.GetMaxVersion(Settings.ChangedEntityIndexingDelay);
        cursor ??= new (ContactId.None, 0);
        return await ContactsBackend.ListChangedPeerContacts(new ChangedContactsQuery {
                MinVersion = cursor.LastUpdatedVersion,
                MaxVersion = maxVersion,
                LastId = cursor.LastUpdatedId,
                Limit = BatchSize,
            },
            cancellationToken);
    }

    protected override async Task ProcessBatch(IReadOnlyList<Contact> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);

        // TODO(FC): in one request
        var indexedUsers = batch.Select(x => new IndexedUser(x.UserId)).Distinct().ToApiArray();
        await IndexedDocuments
            .UpsertPartially<IndexedUser, IIndexedUserMinimalUpsert, UserId>(x => x.UserIndexName,
                indexedUsers,
                cancellationToken)
            .ConfigureAwait(false);
        var indexedUserContacts = batch.Select(x => x.ToIndexedUserContact()).ToApiArray();
        await IndexedDocuments.SaveUserContacts(indexedUserContacts, [], cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<IndexingFlowTransitionKind> HandleTail(int processedCount, CancellationToken cancellationToken)
    {
        var transition = await base.HandleTail(processedCount, cancellationToken).ConfigureAwait(false);
        if (processedCount > 0) {
            Log.LogInformation("`{Id}`.OnTailReached: requesting user index refresh", Id);
            await Host.Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshUsers: true), cancellationToken)
                .ConfigureAwait(false);
        }
        return transition;
    }
}
