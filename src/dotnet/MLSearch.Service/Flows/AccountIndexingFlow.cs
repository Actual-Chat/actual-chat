using ActualChat.Contacts;
using ActualChat.Flows;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Module;
using ActualChat.Queues;
using ActualChat.Search;
using ActualChat.Users;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class AccountIndexingFlow : BatchedIndexingFlowBase<AccountFull, UserId>, IMasterFlow
{
    protected override int CurrentFlowSetVersion => 1;
    protected override TimeSpan RecheckInterval => Host.Services.GetRequiredService<MLSearchSettings>().IndexingRecheckInterval;
    [field: AllowNull, MaybeNull]
    private IAccountsBackend AccountsBackend => field ??= Host.Services.GetRequiredService<IAccountsBackend>();
    [field: AllowNull, MaybeNull]
    private IContactsBackend ContactsBackend => field ??= Host.Services.GetRequiredService<IContactsBackend>();
    [field: AllowNull, MaybeNull]
    private IndexedDocuments IndexedDocuments => field ??= Host.Services.GetRequiredService<IndexedDocuments>();
    [field: AllowNull, MaybeNull]
    private MLSearchSettings Settings => field ??= Host.Services.GetRequiredService<MLSearchSettings>();
    [field: AllowNull, MaybeNull]
    private Task WhenReady => field ??= Host.Services.GetRequiredService<OpenSearchConfigurator>().WhenCompleted;

    protected override async Task<IReadOnlyList<AccountFull>> GetBatch(
        IndexingFlowCursor<UserId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = Clocks.GetMaxVersion(Settings.IndexingDelay);
        cursor ??= new (UserId.None, 0);
        var batch = await AccountsBackend.ListChangedFull(
                cursor.LastUpdatedVersion,
                maxVersion,
                cursor.LastUpdatedId,
                BatchSize,
                cancellationToken)
            .ConfigureAwait(false);
        DebugLog?.LogDebug("`{Id}`.GetBatch: retrieved {Count} items with maxVersion={MaxVersion}, cursor={Cursor}", Id, batch.Count, maxVersion, cursor);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<AccountFull> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);

        var updated = await batch.Select(ToIndexedUserContact).Collect(cancellationToken).ConfigureAwait(false);
        await IndexedDocuments.UpdateUserContacts(updated, [], cancellationToken).ConfigureAwait(false);
        return;

        async Task<IndexedUserContact> ToIndexedUserContact(AccountFull account)
        {
            var placeIds = await ContactsBackend.ListPlaceIds(account.Id, cancellationToken).ConfigureAwait(false);
            return account.ToIndexedUserContact(placeIds);
        }
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
