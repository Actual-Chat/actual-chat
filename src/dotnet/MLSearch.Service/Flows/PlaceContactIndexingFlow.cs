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
public partial class PlaceContactIndexingFlow : BatchedIndexingFlowBase<Contact, ContactId>, IMasterFlow
{
    protected override int CurrentFlowSetVersion => 1;
    protected override TimeSpan RecheckInterval => Settings.IndexingTailRecheckInterval;

    [field: AllowNull, MaybeNull]
    private IContactsBackend ContactsBackend => field ??= Host.Services.GetRequiredService<IContactsBackend>();
    [field: AllowNull, MaybeNull]
    private IndexedDocuments IndexedDocuments => field ??= Host.Services.GetRequiredService<IndexedDocuments>();
    [field: AllowNull, MaybeNull]
    private MLSearchSettings Settings => field ??= Host.Services.GetRequiredService<MLSearchSettings>();
    [field: AllowNull, MaybeNull]
    private Task WhenReady => field ??= Host.Services.GetRequiredService<OpenSearchConfigurator>().WhenCompleted;

    protected override async Task<IReadOnlyList<Contact>> GetBatch(
        IndexingFlowCursor<ContactId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = Clocks.GetMaxVersion(Settings.ChangedEntityIndexingDelay);
        cursor ??= new (ContactId.None, 0);
        var batch = await ContactsBackend.ListChangedPlaceContacts(
                new ChangedContactsQuery {
                    MinVersion = cursor.LastUpdatedVersion,
                    MaxVersion = maxVersion,
                    LastId = cursor.LastUpdatedId,
                    Limit = BatchSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
        DebugLog?.LogDebug(
            "`{Id}`.GetBatch: retrieved {Count} items with maxVersion={MaxVersion}, cursor={Cursor}",
            Id, batch.Length, maxVersion, cursor);
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
