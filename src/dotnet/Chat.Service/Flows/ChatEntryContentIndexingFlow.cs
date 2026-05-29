using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

// Indexes content derived from a chat entry itself (currently links; polls etc. may be added later).

[Flow(DelayQuanta = 30)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class ChatEntryContentIndexingFlow : BatchedIndexingFlow<ChatEntry, ChatEntryId>
{
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IMarkupParser MarkupParser => field ??= Services.GetRequiredService<IMarkupParser>();
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
            .Call(new ChatsBackend_UpdateChatLinkIndex(ChatId, entryIds, items), cancellationToken)
            .ConfigureAwait(false);

        Log.LogInformation(
            "[ChatEntryContentIndexingFlow] ProcessBatch <-- ChatId={ChatId}, Count={Count}, EntryIds={EntryIds}, Items={Items}, Duration={Duration}",
            ChatId, batch.Count, entryIds.Length, items.Length, startedAt.Elapsed.ToShortString());
    }

    // Re-extracts URLs from the current markup and stores the URL directly on the
    // LinkItem so the UI can always render at least a plain <a>, even if the
    // LinkPreview never resolved. Trusted-GIF URLs are filtered out — they render
    // inline as <img> (UrlMarkupView) and have no business in the Links tab.
    private IEnumerable<LinkItem> ExtractItems(ChatEntry entry)
    {
        var localIndex = 0;
        foreach (var url in MarkupParser.ExtractLinks(entry.Content)) {
            if (UrlMapper.IsTrustedGifUrl(url))
                continue;

            yield return new LinkItem {
                Id = Symbol.Empty,
                EntryId = entry.Id,
                LocalIndex = localIndex++,
                At = entry.BeginsAt,
                Url = url,
                LinkPreviewId = LinkPreview.ComposeId(url),
            };
        }
    }
}
