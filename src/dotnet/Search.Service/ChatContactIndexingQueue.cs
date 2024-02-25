using ActualChat.Chat;
using ActualChat.Mesh;
using ActualChat.Redis;
using ActualChat.Search.Db;
using ActualChat.Search.Module;
using ActualLab.Interception;

namespace ActualChat.Search;

public class ChatContactIndexingQueue(IServiceProvider services) : ActivatedWorkerBase(services), INotifyInitialized
{
    private const int SyncBatchSize = 1000;
    private SearchSettings? _settings;
    private IChatsBackend? _chatsBackend;
    private IContactIndexStatesBackend? _indexedChatsBackend;
    private ElasticConfigurator? _elasticConfigurator;
    private IMeshLocks<SearchDbContext>? _meshLocks;
    private ICommander? _commander;

    private SearchSettings Settings => _settings ??= Services.GetRequiredService<SearchSettings>();
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
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

        await Sync(SyncChatsUnsafe, "ChatContactIndexing", cancellationToken).ConfigureAwait(false);
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

    private async Task SyncChatsUnsafe(CancellationToken cancellationToken)
    {
        await SyncNewChats(cancellationToken).ConfigureAwait(false);
        await SyncUpdatedChats(cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncNewChats(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Default.Region();
        var state = await ContactIndexStatesBackend.GetForChats(cancellationToken).ConfigureAwait(false);
        var batches = ChatsBackend
            .Batch(state.LastCreatedAt,
                state.LastUpdatedChatId,
                SyncBatchSize,
                cancellationToken);
        await foreach (var chats in batches.ConfigureAwait(false)) {
            var updates = chats.Select(x => x.ToIndexedChatContact()).ToApiArray();
            var indexCmd = new SearchBackend_ChatContactBulkIndex(updates, ApiArray<IndexedChatContact>.Empty);
            await Commander.Call(indexCmd, cancellationToken)
                .ConfigureAwait(false);

            var last = chats[^1];
            state = state with { LastCreatedId = last.Id, LastCreatedAt = last.CreatedAt};
            state = await SaveState(state, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SyncUpdatedChats(CancellationToken cancellationToken)
    {
        var state = await ContactIndexStatesBackend.GetForChats(cancellationToken).ConfigureAwait(false);
        var batches = ChatsBackend
            .BatchUpdates(
                state.LastCreatedAt,
                state.LastUpdatedVersion,
                state.LastUpdatedChatId,
                SyncBatchSize,
                cancellationToken);
        await foreach (var chats in batches.ConfigureAwait(false)) {
            var updates = chats.Select(x => x.ToIndexedChatContact()).ToApiArray();
            var indexCmd = new SearchBackend_ChatContactBulkIndex(updates, []);
            await Commander.Call(indexCmd, cancellationToken)
                .ConfigureAwait(false);

            var last = chats[^1];
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
