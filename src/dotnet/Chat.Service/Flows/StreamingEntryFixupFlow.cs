using ActualChat.Chat.Db;
using ActualChat.Flows;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Closes or removes chat entries stuck in the streaming state for longer than
/// <see cref="Constants.Chat.StreamingEntryFixupDelay"/>. Handles the failure modes the
/// transcription pipeline can't self-recover from: the node that owned the stream died,
/// or the pipeline threw.
/// </summary>
// A stuck entry renders as a permanent ellipsis for everyone but its author, and every client
// watching it retries its transcript stream forever - so this flow is what ends both.
//
// It's a master flow on purpose: its predecessor was per-chat and woke up only via a delayed
// event scheduled on entry creation, so a single lost event starved that chat's fixup
// indefinitely (dev had entries stuck since May). MasterFlowStarter re-schedules a master flow
// on every shard acquisition, and FlowBackend.ListStats reports a periodic flow with no pending
// resume event as Stuck rather than Idle.
[Flow(DelayQuanta = 30, ResumeTimeout = 60)]
[DataContract, MessagePackObject(true)]
public sealed partial class StreamingEntryFixupFlow : PeriodicFlow, IMasterFlow
{
    private const int BatchSize = 50;
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(1);

    private DbHub<ChatDbContext> DbHub => field ??= Services.DbHub<ChatDbContext>();
    private ICommander Commander => field ??= Services.Commander();

    [DataMember(Order = 0), Key(0)]
    public long TotalClosed { get; set; }
    [DataMember(Order = 1), Key(1)]
    public long TotalRemoved { get; set; }

    protected override async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        var stale = await ListStale(1, cancellationToken).ConfigureAwait(false);
        return stale.Count == 0
            ? new FlowReadiness("No stale streaming entries", RunInterval)
            : FlowReadiness.Ready;
    }

    protected override async ValueTask<Moment> Run(CancellationToken cancellationToken)
    {
        var stale = await ListStale(BatchSize, cancellationToken).ConfigureAwait(false);
        if (stale.Count == 0)
            return Hub.SystemNow + RunInterval;

        Console.Log($"Fixing {stale.Count} stale streaming entries");
        var now = Hub.SystemNow;
        var closedCount = 0;
        var removedCount = 0;
        var failedCount = 0;
        foreach (var entry in stale) {
            // An entry with no content never got a transcript, so there's nothing to keep.
            var mustRemove = entry.Content.IsNullOrEmpty();
            var change = mustRemove
                ? Change.Remove<ChatEntryDiff>()
                : Change.Update(new ChatEntryDiff {
                    ContentStreamId = "",
                    EndsAt = now,
                });
            var command = new ChatsBackend_ChangeEntry(ChatEntryId.Parse(entry.Id), null, change);
            try {
                await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
                if (mustRemove)
                    removedCount++;
                else
                    closedCount++;
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                Console.LogError($"Failed to fix up entry #{entry.Id}: {e.Message}", e);
                failedCount++;
            }
        }

        TotalClosed += closedCount;
        TotalRemoved += removedCount;
        Console.Log($"Fix-up done: closed {closedCount}, removed {removedCount}, failed {failedCount}; "
            + $"totals {TotalClosed}/{TotalRemoved}");

        // A full batch means there may be more; anything else - including a batch we couldn't
        // fully fix - waits out the interval, so a permanently failing entry can't spin the flow.
        return stale.Count == BatchSize && failedCount == 0
            ? Hub.SystemNow
            : Hub.SystemNow + RunInterval;
    }

    // Private methods

    private async Task<List<DbChatEntry>> ListStale(int limit, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var staleBefore = (Hub.SystemNow - Constants.Chat.StreamingEntryFixupDelay).ToDateTime();
        return await dbContext.ChatEntries
            .Where(e => e.Kind == 0
                && !e.IsRemoved
                && e.ContentStreamId != null
                && e.ContentStreamId != ""
                && e.BeginsAt < staleBefore)
            .OrderBy(e => e.BeginsAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
