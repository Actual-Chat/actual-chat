using ActualChat.Chat.Db;
using ActualChat.Flows;
using ActualChat.Media;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Migrates audio entries to the media table and sets MediaId on corresponding text entries.
/// Processes one chat at a time, in batches of 100 text entries per resume.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class ChatEntryMigrationFlow : Flow<(Moment, long, long)>
{
    private const int BatchSize = 50;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(2).ToRandom(0.25);

    private DbHub<ChatDbContext> DbHub => field ??= Services.DbHub<ChatDbContext>();
    private ICommander Commander => Hub.Commander;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public string? LastProcessedChatId { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public long LastProcessedLocalId { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public long MigratedEntryCount { get; set; }
    [DataMember(Order = 3), MemoryPackOrder(3)]
    public long MigratedChatCount { get; set; }
    [DataMember(Order = 4), MemoryPackOrder(4)]
    public long TotalEntryCount { get; set; }
    [DataMember(Order = 5), MemoryPackOrder(5)]
    public long TotalChatCount { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        if (TotalEntryCount == 0)
            TotalEntryCount = Math.Max(1, await dbContext.ChatEntries
                .CountAsync(x => x.Kind == 1, cancellationToken) // 1 = legacy Audio kind
                .ConfigureAwait(false));
        if (TotalChatCount == 0)
            TotalChatCount = Math.Max(1, await dbContext.Chats
                .CountAsync(cancellationToken)
                .ConfigureAwait(false));

        // Get next chat to process
        var chatQuery = dbContext.Chats.OrderBy(x => x.Id);
        if (!LastProcessedChatId.IsNullOrEmpty())
            chatQuery = (IOrderedQueryable<DbChat>)chatQuery.Where(x => string.Compare(x.Id, LastProcessedChatId) > 0);

        var chatId = await chatQuery
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (chatId == null) {
            Complete();
            return;
        }

        // Get batch of text entries that have AudioEntryId set but no MediaId yet
        var batch = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId
                && e.Kind == 0 // 0 = Text kind
                && e.AudioEntryId != null
                && (e.AudioId == null || e.AudioId == "")) // Skipping already migrated entries
            .OrderBy(e => e.LocalId)
            .Where(e => e.LocalId > LastProcessedLocalId)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (batch.Count == 0) {
            // Current chat done, move to next
            LastProcessedLocalId = 0;
            LastProcessedChatId = chatId;
            MigratedChatCount++;
            Console.Log($"Chat #{chatId} done ({MigratedChatCount}/{TotalChatCount})");
            Runtime.StageResumeIn(BatchDelay.Next());
            return;
        }

        try {
            var migratedInBatch = 0;
            foreach (var textEntry in batch) {
                try {
                    await ProcessOne(dbContext, textEntry, cancellationToken).ConfigureAwait(false);
                    migratedInBatch++;
                }
                catch (Exception e) {
                    Console.LogError($"Failed to process entry #{textEntry.Id}: {e.Message}", e);
                }
            }
            LastProcessedLocalId = batch[^1].LocalId;
            MigratedEntryCount += migratedInBatch;
        }
        finally {
            // If MigrateEntry fails, we still need to save the entries migrated earlier
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var progress = (double)MigratedEntryCount / TotalEntryCount * 100;
        Console.Log(
            $"Progress: {progress:F1}% "
            + $"({MigratedChatCount}/{TotalChatCount} chats, {MigratedEntryCount}/{TotalEntryCount} entries), "
            + $"processing chat #{chatId}");

        if (batch.Count < BatchSize) {
            // Current chat done
            LastProcessedLocalId = 0;
            LastProcessedChatId = chatId;
            MigratedChatCount++;
        }

        Runtime.StageResumeIn(BatchDelay.Next());
    }

    protected virtual void Complete()
    {
        Console.Log($"Completed, processed {MigratedChatCount} chats, {MigratedEntryCount} entries");
        SetResult((Hub.Clocks.SystemClock.Now, MigratedChatCount, MigratedEntryCount));
    }

    protected virtual async Task ProcessOne(ChatDbContext dbContext, DbChatEntry textEntry, CancellationToken cancellationToken)
    {
        var chatId = ChatId.Parse(textEntry.ChatId);
        var audioEntryLid = textEntry.AudioEntryId!.Value;

        // Look up the corresponding audio entry
        // Legacy audio entries had kind=1, construct ID directly
        var audioEntryDbId = $"{chatId.Value}:1:{audioEntryLid.Format()}";
        var audioEntry = await dbContext.ChatEntries
            .FirstOrDefaultAsync(e => e.Id == audioEntryDbId, cancellationToken)
            .ConfigureAwait(false);

        if (audioEntry == null) {
            Console.LogWarning($"Audio entry {audioEntryDbId} not found for text entry {textEntry.Id}");
            return;
        }

        var audioBlobId = audioEntry.Content;
        if (audioBlobId.IsNullOrEmpty()) {
            // Audio entry has no blob yet (not finalized), skip
            return;
        }

        // Create Media record
        var mediaId = MediaId.New(chatId.Value);
        var beginsAt = audioEntry.BeginsAt.ToMoment();
        var endsAt = audioEntry.EndsAt?.ToMoment() ?? beginsAt;
        var contentEndsAt = audioEntry.ContentEndsAt?.ToMoment() ?? endsAt;

        var media = new MediaFull(mediaId) {
            BlobId = audioBlobId,
            Kind = MediaKind.ChatEntryAudio,
            ContentType = "audio/webm",
            BeginsAt = beginsAt,
            EndsAt = endsAt,
            ContentEndsAt = contentEndsAt,
        };
        var changeCommand = new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media });

        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);

        // Update text entry's AudioId directly in DB
        textEntry.AudioId = mediaId.Value;
    }
}
