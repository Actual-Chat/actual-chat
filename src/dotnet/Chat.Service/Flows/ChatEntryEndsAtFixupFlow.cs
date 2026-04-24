using ActualChat.Chat.Db;
using ActualChat.Flows;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Fixes up text entries that have AudioId (MediaId) set but null EndsAt.
/// This happens because <see cref="ChatEntryMigrationFlow"/> copies AudioId
/// from legacy audio entries to text entries but doesn't set EndsAt.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(AllowPrivate = true)]
public partial class ChatEntryEndsAtFixupFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 200;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(2).ToRandom(0.25);

    [IgnoreMember] private DbHub<ChatDbContext> DbHub => field ??= Services.DbHub<ChatDbContext>();
    [IgnoreMember] private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public string? LastProcessedEntryId { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public long FixedCount { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public long SkippedCount { get; set; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public long TotalScanned { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Query text entries (Kind=0) that have AudioId set but EndsAt is null
        var query = dbContext.ChatEntries
            .Where(e => e.Kind == 0
                && e.AudioId != null
                && e.EndsAt == null);

        if (!LastProcessedEntryId.IsNullOrEmpty())
            query = query.Where(e => string.Compare(e.Id, LastProcessedEntryId) > 0);

        var batch = await query
            .OrderBy(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (batch.Count == 0) {
            Console.Log($"Completed: fixed {FixedCount}, skipped {SkippedCount}, scanned {TotalScanned}");
            SetResult((Hub.Clocks.SystemClock.Now, FixedCount));
            return;
        }

        var fixedInBatch = 0;
        var skippedInBatch = 0;
        foreach (var entry in batch) {
            // Only process entries where AudioId is a valid MediaId (not a StreamId)
            if (!MediaId.TryParse(entry.AudioId!, out var mediaId)) {
                skippedInBatch++;
                continue;
            }

            try {
                var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
                if (media is null || media.EndsAt == default) {
                    skippedInBatch++;
                    continue;
                }

                var endsAt = (DateTime)media.EndsAt;
                entry.EndsAt = endsAt;
                entry.Duration = (endsAt - entry.BeginsAt).TotalSeconds;
                fixedInBatch++;
            }
            catch (Exception e) {
                Console.LogError($"Failed to process entry {entry.Id}: {e.Message}", e);
                skippedInBatch++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LastProcessedEntryId = batch[^1].Id;
        FixedCount += fixedInBatch;
        SkippedCount += skippedInBatch;
        TotalScanned += batch.Count;

        Console.Log($"Progress: fixed {FixedCount}, skipped {SkippedCount}, scanned {TotalScanned}");
        Runtime.StageResumeIn(BatchDelay.Next());
    }
}
