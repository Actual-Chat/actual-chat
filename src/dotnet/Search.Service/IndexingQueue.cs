using ActualChat.Chat;
using ActualChat.Mesh;
using ActualChat.Redis;
using ActualChat.Search.Db;
using ActualChat.Search.Module;
using ActualLab.Interception;

namespace ActualChat.Search;


public class IndexingQueue(IServiceProvider services) : WorkerBase, IHasServices, INotifyInitialized
{
    private const int ChatDispatchBatchSize = 20;
    private const int IndexChatSyncBatchSize = 1000;
    private const int EntryBatchSize = 1000;
    private static readonly TimeSpan MaxIdleInterval = TimeSpan.FromMinutes(5);
    private SearchSettings? _settings;
    private IChatsBackend? _chatsBackend;
    private IIndexedChatsBackend? _indexedChatsBackend;
    private ElasticConfigurator? _elasticConfigurator;
    private IMeshLocks<SearchDbContext>? _meshLocks;
    private ICommander? _commander;
    private ILogger? _log;

    public IServiceProvider Services { get; } = services;

    private SearchSettings Settings => _settings ??= Services.GetRequiredService<SearchSettings>();
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private ElasticConfigurator ElasticConfigurator => _elasticConfigurator ??= Services.GetRequiredService<ElasticConfigurator>();

    private IIndexedChatsBackend IndexedChatsBackend
        => _indexedChatsBackend ??= Services.GetRequiredService<IIndexedChatsBackend>();

    private IMeshLocks<SearchDbContext> MeshLocks
        => _meshLocks ??= Services.GetRequiredService<IMeshLocks<SearchDbContext>>();

    private ICommander Commander => _commander ??= Services.Commander();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    void INotifyInitialized.Initialized()
        => this.Start();

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        if (!Settings.IsSearchEnabled)
            return Task.CompletedTask;

        var retryDelays = RetryDelaySeq.Exp(5, MaxIdleInterval.TotalSeconds);
        return AsyncChain.From(SyncHistory)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .Run(cancellationToken);
    }

    private async Task SyncHistory(CancellationToken cancellationToken)
    {
        try {
            using var _1 = Tracer.Default.Region();
            if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
                await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);
            await MeshLocks.Run(EnsureIndexedChatsCreated, "IndexedChatsSync", cancellationToken)
                .ConfigureAwait(false);

            await IndexAllChats(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }

    private async Task IndexAllChats(CancellationToken cancellationToken)
    {
        using var _2 = Tracer.Default.Region();
        var batches = IndexedChatsBackend.Batches(Moment.MinValue, ChatId.None, ChatDispatchBatchSize, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var indexedChats in batches)
        foreach (var indexedChat in indexedChats)
            // skip indexing if it is already started on another replica
            await MeshLocks
                .TryRun(ct1 => IndexChat(indexedChat.Id, ct1), $"ChatHistoryIndex.{indexedChat.Id}", cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task EnsureIndexedChatsCreated(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Default.Region();
        var last = await IndexedChatsBackend.GetLast(cancellationToken).ConfigureAwait(false);
        var batches = ChatsBackend.Batches(
                last?.ChatCreatedAt ?? Moment.MinValue,
                last?.Id ?? ChatId.None,
                IndexChatSyncBatchSize,
                cancellationToken)
            .ConfigureAwait(false);
        await foreach (var chats in batches) {
            var changes = chats.Select(ToChange).ToApiArray();
            var cmd = new IndexedChatsBackend_BulkChange(changes);
            await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        }
        return;

        IndexedChatChange ToChange(Chat.Chat chat)
            => new (chat.Id, null, Change.Create(new IndexedChat(chat.Id) { ChatCreatedAt = chat.CreatedAt, }));
    }

    private async Task IndexChat(ChatId chatId, CancellationToken cancellationToken)
    {
        var hasChanges = await IndexNewEntries(chatId, cancellationToken).ConfigureAwait(false);
        hasChanges |= await IndexUpdatedAndRemovedEntries(chatId, cancellationToken).ConfigureAwait(false);
        if (hasChanges)
            await Commander.Call(new SearchBackend_Refresh(chatId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IndexNewEntries(ChatId chatId, CancellationToken cancellationToken)
    {
        var news = await ChatsBackend.GetNews(chatId, cancellationToken).ConfigureAwait(false);
        var lastLid = (news.TextEntryIdRange.End - 1).Clamp(0, long.MaxValue);
        var indexedChat = await IndexedChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        var lastIndexedLid = indexedChat.LastEntryLocalId;
        if (lastIndexedLid >= lastLid)
            return false;

        var idTiles =
            Constants.Chat.ServerIdTileStack.LastLayer.GetCoveringTiles(
                news.TextEntryIdRange.WithStart(lastIndexedLid));
        foreach (var tile in idTiles) {
            var chatTile = await ChatsBackend.GetTile(chatId,
                    ChatEntryKind.Text,
                    tile.Range,
                    false,
                    cancellationToken)
                .ConfigureAwait(false);
            var entries = chatTile.Entries.Where(x => !x.Content.IsNullOrEmpty())
                .Select(x => new IndexedEntry {
                    Id = new TextEntryId(chatId, x.LocalId, AssumeValid.Option),
                    Content = x.Content,
                    ChatId = chatId,
                })
                .ToApiArray();
            await Commander.Call(new SearchBackend_EntryBulkIndex(chatId, entries, ApiArray<IndexedEntry>.Empty), cancellationToken)
                .ConfigureAwait(false);

            indexedChat = await SaveIndexedChat(indexedChat with { LastEntryLocalId = chatTile.Entries[^1].LocalId, },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        return true;
    }

    private async Task<bool> IndexUpdatedAndRemovedEntries(ChatId chatId, CancellationToken cancellationToken)
    {
        var indexedChat = await IndexedChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        var maxEntryVersion = await ChatsBackend.GetMaxEntryVersion(indexedChat.Id, cancellationToken).ConfigureAwait(false) ?? 0;
        if (indexedChat.LastEntryVersion >= maxEntryVersion)
            return false;

        while (!cancellationToken.IsCancellationRequested) {
            var changedEntries = await ChatsBackend
                .ListChangedEntries(indexedChat.Id,
                    EntryBatchSize,
                    indexedChat.LastEntryLocalId,
                    indexedChat.LastEntryVersion,
                    cancellationToken)
                .ConfigureAwait(false);
            var updated = changedEntries.Where(x => !x.IsRemoved && !x.Content.IsNullOrEmpty())
                .Select(x => new IndexedEntry {
                    Id = new TextEntryId(chatId, x.LocalId, AssumeValid.Option),
                    Content = x.Content,
                    ChatId = chatId,
                })
                .ToApiArray();
            var removed = changedEntries.Where(x => x.IsRemoved || x.Content.IsNullOrEmpty())
                .Select(x => new IndexedEntry {
                    Id = x.Id.AsTextEntryId(),
                })
                .ToApiArray();
            await Commander.Call(new SearchBackend_EntryBulkIndex(indexedChat.Id, updated, removed), cancellationToken)
                .ConfigureAwait(false);
            indexedChat = await SaveIndexedChat(indexedChat with {
                        LastEntryVersion = changedEntries[^1].Version,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        return true;
    }

    private async Task<IndexedChat> SaveIndexedChat(IndexedChat indexedChat, CancellationToken cancellationToken)
    {
        var change = new IndexedChatChange(indexedChat.Id, indexedChat.Version, Change.Update(indexedChat));
        var cmd = new IndexedChatsBackend_BulkChange(ApiArray.New(change));
        var indexedChats = await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        return indexedChats[0]!;
    }
}
