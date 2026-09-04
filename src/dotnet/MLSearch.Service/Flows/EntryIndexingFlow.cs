using ActualChat.Flows;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Module;
using ActualChat.Queues;
using ActualChat.Search;

namespace ActualChat.MLSearch.Flows;

[Flow(DelayQuanta = 30)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class EntryIndexingFlow : BatchedIndexingFlow<ChatEntry, ChatEntryId>
{
    private MLSearchSettings Settings => field ??= Services.GetRequiredService<MLSearchSettings>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IPlacesBackend PlacesBackend => field ??= Services.GetRequiredService<IPlacesBackend>();
    private IndexedDocuments IndexedDocuments => field ??= Services.GetRequiredService<IndexedDocuments>();
    private IMarkupParser MarkupParser => field ??= Services.GetRequiredService<IMarkupParser>();
    private Task WhenReady => field ??= Services.GetRequiredService<OpenSearchConfigurator>().WhenReady;

    private ChatId ChatId => field ??= ChatId.Parse(Id.Arguments);

    protected override async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        var readiness = await PrepareOnce().ConfigureAwait(false);
        if (readiness.IsSuspended) {
            // Let's wait a bit for chat to appear & retry
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            readiness = await PrepareOnce().ConfigureAwait(false);
        }
        return readiness;

        async ValueTask<FlowReadiness> PrepareOnce() {
            var chat = await ChatsBackend.Get(ChatId, cancellationToken).ConfigureAwait(false);
            if (chat is null)
                return "Chat doesn't exist";

            Place? place = null;
            if (chat.Id is PlaceChatId placeChatId) {
                place = await PlacesBackend.Get(placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
                if (place is null)
                    return "Chat's Place doesn't exist";
            }

            var indexedChat = chat.ToIndexedChat(place);
            await IndexedDocuments.SaveChats([indexedChat], cancellationToken).ConfigureAwait(false);
            return FlowReadiness.Ready;
        }
    }

    protected override async Task<IReadOnlyList<ChatEntry>> GetBatch(
        IndexingFlowCursor<ChatEntryId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = ResumedAt.ToVersion(-Settings.ChangedEntityIndexingDelay);
        cursor ??= new(ChatEntryId.New(ChatId, 0), 0);
        var batch = await ChatsBackend.ListChangedEntries(new ChangedEntriesQuery {
                    ChatId = ChatId,
                    LastLocalId = cursor.LastUpdatedId?.LocalId ?? 0,
                    MinVersion = cursor.LastUpdatedVersion,
                    MaxVersion = maxVersion,
                    Limit = BatchSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<ChatEntry> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);

        var live = batch
            .Where(x => x is { IsRemoved: false, IsSystemEntry: false })
            .ToList();
        var authorUserIds = new Dictionary<AuthorId, UserId?>();
        foreach (var authorId in live.Select(x => x.AuthorId).Distinct()) {
            var author = await AuthorsBackend.Get(ChatId, authorId, RequestedAuthorKind.Full, cancellationToken)
                .ConfigureAwait(false);
            authorUserIds[authorId] = author?.UserId;
        }
        var updated = live
            .Select(x => x.ToIndexedEntry(MarkupParser, authorUserIds.GetValueOrDefault(x.AuthorId)))
            .ToList();
        var removed = batch
            .Where(x => x is { IsRemoved: true, IsSystemEntry: false })
            .Select(x => x.Id)
            .ToList();
        await IndexedDocuments.SaveEntries(updated, removed, cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask TailReached(bool hasProcessedAnyItems, CancellationToken cancellationToken)
    {
        await base.TailReached(hasProcessedAnyItems, cancellationToken).ConfigureAwait(false);
        if (hasProcessedAnyItems) {
            Console.Log("Requesting entry index refresh");
            await Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshEntries: true), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
