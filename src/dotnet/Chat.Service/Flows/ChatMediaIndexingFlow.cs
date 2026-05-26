using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

// Indexes media attachments (photos, videos, files) of a chat. Media is indexed only once its
// blob is uploaded; entries with not-yet-uploaded media are rechecked via a delayed self-resume.

[Flow(DelayQuanta = 30)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class ChatMediaIndexingFlow : BatchedIndexingFlow<ChatEntry, ChatEntryId>
{
    private const ChatContentKind MediaKinds = ChatContentKind.Photo | ChatContentKind.Video | ChatContentKind.File;
    private static readonly TimeSpan PendingRecheckDelay = TimeSpan.FromSeconds(20);
    // Entries with media still !IsReady past this age are treated as permanently broken
    // (e.g. metadata row created but blob never uploaded) and dropped from PendingEntryLids.
    private static readonly TimeSpan PendingMaxAge = TimeSpan.FromDays(7);
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;

    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private ICommander Commander => field ??= Services.Commander();
    private ChatId ChatId => field ??= ChatId.Parse(Id.Arguments);
    private ILogger Log => field ??= Services.LogFor(GetType());

    [DataMember(Order = 10), MemoryPackOrder(10), Key(10)]
    public long[] PendingEntryLids { get; set; } = [];

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
            "[ChatMediaIndexingFlow] ProcessBatch --> ChatId={ChatId}, First=#{FirstLid} v{FirstVersion}, Last=#{LastLid} v{LastVersion}",
            ChatId, first.LocalId, first.Version, last.LocalId, last.Version);

        var entries = batch.Where(x => !x.IsSystemEntry).ToList();
        if (entries.Count == 0) {
            Log.LogInformation(
                "[ChatMediaIndexingFlow] ProcessBatch <-- ChatId={ChatId}, Count={Count}, Entries=0 (no-op), Duration={Duration}",
                ChatId, batch.Count, startedAt.Elapsed.ToShortString());
            return;
        }

        var entryIds = entries.Select(x => x.Id).ToArray();
        var liveLids = entries.Where(x => !x.IsRemoved).Select(x => x.LocalId).ToList();
        var notReadyLids = await IndexEntries(entryIds, liveLids, cancellationToken).ConfigureAwait(false);
        PendingEntryLids = PendingEntryLids.Concat(notReadyLids).Distinct().ToArray();

        Log.LogInformation(
            "[ChatMediaIndexingFlow] ProcessBatch <-- ChatId={ChatId}, Count={Count}, EntryIds={EntryIds}, LiveLids={LiveLids}, NotReady={NotReady}, Pending={Pending}, Duration={Duration}",
            ChatId, batch.Count, entryIds.Length, liveLids.Count, notReadyLids.Count, PendingEntryLids.Length, startedAt.Elapsed.ToShortString());
    }

    protected override async ValueTask TailReached(bool hasProcessedAnyItems, CancellationToken cancellationToken)
    {
        if (PendingEntryLids.Length == 0)
            return;

        var pendingLids = PendingEntryLids;
        var entryIds = pendingLids.Select(lid => ChatEntryId.New(ChatId, lid)).ToArray();
        var notReadyLids = await IndexEntries(entryIds, pendingLids, cancellationToken).ConfigureAwait(false);
        PendingEntryLids = notReadyLids.ToArray();
        if (PendingEntryLids.Length > 0)
            Runtime.StageResumeIn(PendingRecheckDelay);
    }

    private async Task<IReadOnlyList<long>> IndexEntries(
        ChatEntryId[] entryIds,
        IReadOnlyCollection<long> liveLids,
        CancellationToken cancellationToken)
    {
        var entries = await ResolveWithAttachments(liveLids, cancellationToken).ConfigureAwait(false);
        var items = new List<ChatContentItem>();
        var notReadyLids = new List<long>();
        var pendingCutoff = ResumedAt - PendingMaxAge;
        foreach (var entry in entries) {
            var isReady = true;
            foreach (var attachment in entry.Attachments) {
                var media = attachment.Media;
                if (!media.IsReady) {
                    isReady = false;
                    continue;
                }
                items.Add(ToItem(entry, attachment, media));
            }
            if (!isReady) {
                if (entry.BeginsAt >= pendingCutoff)
                    notReadyLids.Add(entry.LocalId);
                else {
                    var brokenMediaIds = entry.Attachments
                        .Where(a => !a.Media.IsReady)
                        .Select(a => a.MediaId.Value)
                        .ToArray();
                    Log.LogWarning(
                        "[ChatMediaIndexingFlow] Dropping not-ready entry from PendingEntryLids: "
                        + "EntryId={EntryId}, BeginsAt={BeginsAt:O}, AgeDays={AgeDays:F1}, BrokenMediaIds={BrokenMediaIds}",
                        entry.Id, entry.BeginsAt.ToDateTime(), (ResumedAt - entry.BeginsAt).TotalDays, brokenMediaIds);
                }
            }
        }
        await Commander
            .Call(new ChatsBackend_UpdateChatContentIndex(ChatId, MediaKinds, entryIds, items.ToArray()), cancellationToken)
            .ConfigureAwait(false);
        return notReadyLids;
    }

    private async Task<IReadOnlyList<ChatEntry>> ResolveWithAttachments(
        IReadOnlyCollection<long> lids,
        CancellationToken cancellationToken)
    {
        if (lids.Count == 0)
            return [];

        var tileRanges = lids.Select(lid => IdTileStack.LastLayer.GetTile(lid).Range).Distinct().ToList();
        var tiles = await tileRanges
            .Select(range => ChatsBackend.GetTile(ChatId, range, true, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        var entryByLid = tiles
            .SelectMany(t => t.Entries)
            .DistinctBy(e => e.LocalId)
            .ToDictionary(e => e.LocalId);
        return lids
            .Select(lid => entryByLid.GetValueOrDefault(lid))
            .SkipNullItems()
            .Where(e => !e.IsRemoved && e.Attachments.Length > 0)
            .ToList();
    }

    private static ChatContentItem ToItem(ChatEntry entry, ChatEntryAttachment attachment, Media.Media media)
        => new() {
            Id = Symbol.Empty,
            Kind = Classify(media.ContentType),
            EntryId = entry.Id,
            LocalIndex = attachment.Index,
            At = entry.BeginsAt,
            MediaId = attachment.MediaId,
            BlobId = media.BlobId,
            ThumbnailMediaId = attachment.ThumbnailMediaId,
            ThumbnailBlobId = attachment.ThumbnailMedia?.BlobId ?? "",
            ContentType = media.ContentType,
            FileName = media.FileName,
            Size = media.Length,
        };

    private static ChatContentKind Classify(string contentType)
        => MediaTypeExt.IsSupportedVideo(contentType) ? ChatContentKind.Video
            : MediaTypeExt.IsSupportedImage(contentType) ? ChatContentKind.Photo
            : ChatContentKind.File;
}
