using ActualChat.Mesh;
using ActualChat.Redis;
using ActualChat.Search.Db;
using ActualChat.Search.Module;
using ActualChat.Users;
using ActualLab.Interception;

namespace ActualChat.Search;

public class UserContactIndexingQueue(IServiceProvider services) : ActivatedWorkerBase(services), INotifyInitialized
{
    private const int SyncBatchSize = 1000;
    private SearchSettings? _settings;
    private IAccountsBackend? _accountsBackend;
    private IContactIndexStatesBackend? _indexedChatsBackend;
    private ElasticConfigurator? _elasticConfigurator;
    private IMeshLocks<SearchDbContext>? _meshLocks;
    private ICommander? _commander;

    private SearchSettings Settings => _settings ??= Services.GetRequiredService<SearchSettings>();
    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();
    private ElasticConfigurator ElasticConfigurator => _elasticConfigurator ??= Services.GetRequiredService<ElasticConfigurator>();

    private IContactIndexStatesBackend ContactIndexStatesBackend
        => _indexedChatsBackend ??= Services.GetRequiredService<IContactIndexStatesBackend>();

    private IMeshLocks<SearchDbContext> MeshLocks
        => _meshLocks ??= Services.GetRequiredService<IMeshLocks<SearchDbContext>>();

    private ICommander Commander => _commander ??= Services.Commander();

    void INotifyInitialized.Initialized()
        => this.Start();

    protected override Task OnRun(CancellationToken cancellationToken)
        => Settings.IsSearchEnabled ? base.OnRun(cancellationToken) : Task.CompletedTask;

    protected override async Task<bool> OnActivate(CancellationToken cancellationToken)
    {
        if (!Settings.IsSearchEnabled )
            return true;

        await Sync(SyncUsersUnsafe, "UserContactIndexing", cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task Sync(Func<CancellationToken, Task> job, string lockKey, CancellationToken cancellationToken)
    {
        try {
            using var _1 = Tracer.Default.Region($"SyncContacts.{lockKey}");
            if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
                await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);
            await MeshLocks.Run(job, lockKey, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }

    private async Task SyncUsersUnsafe(CancellationToken cancellationToken)
    {
        await SyncNewUsers(cancellationToken).ConfigureAwait(false);
        await SyncUpdatedUsers(cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncNewUsers(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Default.Region();
        var state = await ContactIndexStatesBackend.GetForUsers(cancellationToken).ConfigureAwait(false);
        var batches = AccountsBackend
            .Batch(state.LastCreatedAt,
                state.LastUpdatedUserId,
                SyncBatchSize,
                cancellationToken);
        await foreach (var accounts in batches.ConfigureAwait(false)) {
            var updates = accounts.Select(x => x.ToIndexedUserContact()).ToApiArray();
            var indexCmd = new SearchBackend_UserContactBulkIndex(updates, ApiArray<IndexedUserContact>.Empty);
            await Commander.Call(indexCmd, cancellationToken)
                .ConfigureAwait(false);

            var last = accounts[^1];
            state = state with { LastCreatedId = last.Id, LastCreatedAt = last.CreatedAt};
            state = await SaveState(state, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SyncUpdatedUsers(CancellationToken cancellationToken)
    {
        var state = await ContactIndexStatesBackend.GetForUsers(cancellationToken).ConfigureAwait(false);
        var batches = AccountsBackend
            .BatchUpdates(state.LastCreatedAt,
                state.LastUpdatedVersion,
                SyncBatchSize,
                cancellationToken);
        await foreach (var accounts in batches.ConfigureAwait(false)) {
            var updates = accounts.Select(x => x.ToIndexedUserContact()).ToApiArray();
            var indexCmd = new SearchBackend_UserContactBulkIndex(updates, []);
            await Commander.Call(indexCmd, cancellationToken)
                .ConfigureAwait(false);

            var last = accounts[^1];
            state = state with { LastUpdatedId = last.Id, LastUpdatedVersion = last.Version };
            state = await SaveState(state, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ContactIndexState> SaveState(ContactIndexState state, CancellationToken cancellationToken)
    {
        var change = state.IsStored() ? Change.Update(state) : Change.Create(state);
        var cmd = new ContactIndexStatesBackend_Change(state.Version, change);
        return await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }
}
