using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

// Indexes content derived from a chat entry itself (currently links; polls etc. may be added later).

[Flow(DelayQuanta = 30)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class ChatEntryContentIndexingFlow : BatchedIndexingFlow<ChatEntry, ChatEntryId>
{
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private ICommander Commander => field ??= Services.Commander();
    private ChatId ChatId => field ??= ChatId.Parse(Id.Arguments);
    private ILogger Log => field ??= Services.LogFor(GetType());

    protected override int BatchSize => 200;
    protected override int Quota => 2000;

    protected override async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(ChatId, cancellationToken).ConfigureAwait(false);
        return chat is null ? "Chat doesn't exist" : FlowReadiness.Ready;
    }

    protected override async Task<IReadOnlyList<ChatEntry>> GetBatch(
        IndexingFlowCursor<ChatEntryId>? cursor,
        CancellationToken cancellationToken)
    {
        cursor ??= new(ChatEntryId.New(ChatId, 0), 0);
        return await ChatsBackend.ListChangedEntries(new ChangedEntriesQuery {
                ChatId = ChatId,
                LastLocalId = cursor.LastUpdatedId?.LocalId ?? 0,
                MinVersion = cursor.LastUpdatedVersion,
                MaxVersion = ResumedAt.ToVersion(-TimeSpan.FromSeconds(2)),
                Limit = BatchSize,
            }, cancellationToken)
            .ConfigureAwait(false);
    }

    protected override async Task ProcessBatch(IReadOnlyList<ChatEntry> batch, CancellationToken cancellationToken)
    {
        var startedAt = CpuTimestamp.Now;
        var first = batch[0];
        var last = batch[^1];
        Log.LogInformation(
            "[ChatEntryContentIndexingFlow] ProcessBatch --> ChatId={ChatId}, First=#{FirstLid} v{FirstVersion}, Last=#{LastLid} v{LastVersion}",
            ChatId, first.LocalId, first.Version, last.LocalId, last.Version);

        var entries = batch.Where(x => !x.IsSystemEntry).ToList();
        if (entries.Count == 0) {
            Log.LogInformation(
                "[ChatEntryContentIndexingFlow] ProcessBatch <-- ChatId={ChatId}, Count={Count}, Items=0 (no-op), Duration={Duration}",
                ChatId, batch.Count, startedAt.Elapsed.ToShortString());
            return;
        }

        var entryIds = entries.Select(x => x.Id).ToArray();
        var items = entries
            .Where(x => !x.IsRemoved)
            .SelectMany(ExtractItems)
            .ToArray();
        await Commander
            .Call(new ChatsBackend_UpdateChatContentIndex(ChatId, ChatContentKind.Link, entryIds, items), cancellationToken)
            .ConfigureAwait(false);

        Log.LogInformation(
            "[ChatEntryContentIndexingFlow] ProcessBatch <-- ChatId={ChatId}, Count={Count}, EntryIds={EntryIds}, Items={Items}, Duration={Duration}",
            ChatId, batch.Count, entryIds.Length, items.Length, startedAt.Elapsed.ToShortString());
    }

    private static IEnumerable<ChatContentItem> ExtractItems(ChatEntry entry)
    {
        var localIndex = 0;
        foreach (var linkPreviewId in entry.LinkPreviewIds) {
            if (linkPreviewId.IsEmpty)
                continue;

            yield return new ChatContentItem {
                Id = Symbol.Empty,
                Kind = ChatContentKind.Link,
                EntryId = entry.Id,
                LocalIndex = localIndex++,
                At = entry.BeginsAt,
                LinkPreviewId = linkPreviewId,
            };
        }
    }
}
