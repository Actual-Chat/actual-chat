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
public partial class AccountIndexingFlow : BatchedIndexingFlow<AccountFull, UserId>, IMasterFlow
{
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private IndexedDocuments IndexedDocuments => field ??= Services.GetRequiredService<IndexedDocuments>();
    private MLSearchSettings Settings => field ??= Services.GetRequiredService<MLSearchSettings>();
    private Task WhenReady => field ??= Services.GetRequiredService<OpenSearchConfigurator>().WhenReady;

    protected override async Task<IReadOnlyList<AccountFull>> GetBatch(
        IndexingFlowCursor<UserId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = ResumedAt.ToVersion(-Settings.ChangedEntityIndexingDelay);
        cursor ??= new(null, 0);
        var batch = await AccountsBackend.ListChangedFull(
                cursor.LastUpdatedVersion,
                maxVersion,
                cursor.LastUpdatedId,
                BatchSize,
                cancellationToken)
            .ConfigureAwait(false);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<AccountFull> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);

        var updated = batch.Select(x => x.ToIndexedUser()).ToArray();
        await IndexedDocuments
            .UpsertPartially<IndexedUser, IIndexedUserUpsertWithoutPlaces, UserId>(x => x.UserIndexName,
                updated,
                cancellationToken)
            .ConfigureAwait(false);
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
