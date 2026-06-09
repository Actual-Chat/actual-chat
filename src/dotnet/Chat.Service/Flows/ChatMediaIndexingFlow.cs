using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

// Indexes media attachments (visual media + files) of a chat. Each attachment is routed to
// the right index table by its content type. Media is indexed only once its blob is uploaded;
// entries with not-yet-uploaded media are rechecked via a delayed self-resume.

// ResumeTimeout > 15s routes resume events to the SlowQueue (see QueueRef.For) and gives a
// full quota Run time to commit its cursor. Without it a backfill Run that overruns the 15s
// default is cancelled before committing, so it restarts from the same cursor and never advances.
[Flow(DelayQuanta = 30, ResumeTimeout = 5 * 60)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class ChatMediaIndexingFlow : BatchedIndexingFlow<ChatEntry, ChatEntryId>
{
    private static readonly TimeSpan PendingRecheckDelay = TimeSpan.FromSeconds(20);
    // Entries with media still !IsReady past this age are treated as permanently broken
    // (e.g. metadata row created but blob never uploaded) and dropped from PendingEntryLids.
    private static readonly TimeSpan PendingMaxAge = TimeSpan.FromDays(7);

    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private ICommander Commander => field ??= Services.Commander();
    private ChatId ChatId => field ??= ChatId.Parse(Id.Arguments);
    private ILogger Log => field ??= Services.LogFor(GetType());

    [DataMember(Order = 10), MemoryPackOrder(10), Key(10)]
    public long[] PendingEntryLids { get; set; } = [];

    protected override int BatchSize => 500;
    // One batch per Run: the cursor is committed at each Resume, so a batch that fails or times
    // out costs at most one batch of rework instead of discarding a whole multi-batch Run.
    protected override int Quota => BatchSize;

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
                RequireAttachments = true,
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
        var liveEntries = entries.Where(x => !x.IsRemoved).ToList();
        var resolved = await ResolveAttachmentsFor(liveEntries, cancellationToken).ConfigureAwait(false);
        var notReadyLids = await IndexEntries(entryIds, resolved, cancellationToken).ConfigureAwait(false);
        PendingEntryLids = PendingEntryLids.Concat(notReadyLids).Distinct().ToArray();

        Log.LogInformation(
            "[ChatMediaIndexingFlow] ProcessBatch <-- ChatId={ChatId}, Count={Count}, EntryIds={EntryIds}, LiveLids={LiveLids}, NotReady={NotReady}, Pending={Pending}, Duration={Duration}",
            ChatId, batch.Count, entryIds.Length, liveEntries.Count, notReadyLids.Count, PendingEntryLids.Length, startedAt.Elapsed.ToShortString());
    }

    protected override async ValueTask TailReached(bool hasProcessedAnyItems, CancellationToken cancellationToken)
    {
        if (PendingEntryLids.Length == 0)
            return;

        var pendingLids = PendingEntryLids;
        var entryIds = pendingLids.Select(lid => ChatEntryId.New(ChatId, lid)).ToArray();
        // Pending entries are referenced by lid only; reload them (incl. BeginsAt + attachments) by id.
        var pendingEntries = (await ChatsBackend.ListEntries(entryIds, false, cancellationToken).ConfigureAwait(false))
            .Where(e => e.Attachments.Length > 0)
            .ToList();
        var notReadyLids = await IndexEntries(entryIds, pendingEntries, cancellationToken).ConfigureAwait(false);
        PendingEntryLids = notReadyLids.ToArray();
        if (PendingEntryLids.Length > 0)
            Runtime.StageResumeIn(PendingRecheckDelay);
    }

    private async Task<IReadOnlyList<long>> IndexEntries(
        ChatEntryId[] entryIds,
        IReadOnlyList<ChatEntry> entries,
        CancellationToken cancellationToken)
    {
        var visualItems = new List<VisualMediaItem>();
        var fileItems = new List<FileItem>();
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
                if (MediaTypeExt.IsSupportedVisualMedia(media.ContentType))
                    visualItems.Add(ToVisualMediaItem(entry, attachment, media));
                else
                    fileItems.Add(ToFileItem(entry, attachment, media));
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
        await Task.WhenAll(
                Commander.Call(new ChatsBackend_UpdateChatVisualMediaIndex(ChatId, entryIds, visualItems.ToArray()), cancellationToken),
                Commander.Call(new ChatsBackend_UpdateChatFileIndex(ChatId, entryIds, fileItems.ToArray()), cancellationToken))
            .ConfigureAwait(false);
        return notReadyLids;
    }

    // Main path: resolves attachments directly per entry, touching only the entries that have them.
    // Avoids materializing whole id-tiles - critical once the batch holds sparse, attachment-only
    // entries that would otherwise spread across many tiles.
    private async Task<IReadOnlyList<ChatEntry>> ResolveAttachmentsFor(
        IReadOnlyList<ChatEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
            return [];

        var withAttachments = await entries
            .Select(async e => e with {
                Attachments = await ChatsBackend.GetEntryAttachments(e.Id, cancellationToken).ConfigureAwait(false),
            })
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return withAttachments.Where(e => e.Attachments.Length > 0).ToList();
    }

    private static VisualMediaItem ToVisualMediaItem(ChatEntry entry, ChatEntryAttachment attachment, Media.Media media)
        => new() {
            Id = Symbol.Empty,
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

    private static FileItem ToFileItem(ChatEntry entry, ChatEntryAttachment attachment, Media.Media media)
        => new() {
            Id = Symbol.Empty,
            EntryId = entry.Id,
            LocalIndex = attachment.Index,
            At = entry.BeginsAt,
            MediaId = attachment.MediaId,
            BlobId = media.BlobId,
            ContentType = media.ContentType,
            FileName = media.FileName,
            Size = media.Length,
        };
}
