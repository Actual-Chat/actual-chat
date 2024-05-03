using ActualChat.Users;

namespace ActualChat.Search;

public sealed class UserContactIndexer(IServiceProvider services) : ContactIndexer(services)
{
    private IAccountsBackend? _accountsBackend;

    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();

    protected override async Task Sync(CancellationToken cancellationToken)
    {
        if (await SyncChanges(cancellationToken).ConfigureAwait(false))
            await Commander.Call(new SearchBackend_Refresh(true), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SyncChanges(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region();
        var state = await ContactIndexStatesBackend.GetForUsers(cancellationToken).ConfigureAwait(false);
        var batches = AccountsBackend
            .BatchChanged(state.LastUpdatedVersion,
                MaxVersion,
                state.LastUpdatedUserId,
                SyncBatchSize,
                cancellationToken);
        var hasChanges = false;
        await foreach (var accounts in batches.ConfigureAwait(false)) {
            using var _2 = Tracer.Region($"{nameof(SyncChanges)} batch: {accounts.Count} accounts");
            var first = accounts[0];
            var last = accounts[^1];
            Log.LogDebug(
                "Indexing {BatchSize} user contacts [(v={FirstVersion}, #{FirstId})..(v={LastVersion}, #{LastId})]",
                accounts.Count,
                first.Version,
                first.Id,
                last.Version,
                last.Id);
            NeedsSync.Reset();
            var updates = accounts.Select(x => x.ToIndexedUserContact()).ToApiArray();
            var indexCmd = new SearchBackend_UserContactBulkIndex(updates, []);
            await Commander.Call(indexCmd, cancellationToken).ConfigureAwait(false);

            state = state with { LastUpdatedId = last.Id, LastUpdatedVersion = last.Version };
            state = await SaveState(state, cancellationToken).ConfigureAwait(false);
            hasChanges = true;
        }
        return hasChanges;
    }
}
