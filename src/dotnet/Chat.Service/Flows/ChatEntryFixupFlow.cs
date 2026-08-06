using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

// Per-chat watchdog for streaming chat entries: closes or removes entries
// that have been in the streaming state for longer than
// Constants.Chat.StreamingEntryFixupDelay. Handles two failure modes the
// online transcription pipeline can't self-recover from: server process
// died mid-transcription, and an unhandled exception inside the pipeline.
// Kicked from ChatsBackend.OnChatEntryChangedEvent each time a streaming
// entry is observed; multiple kicks within the per-call delayQuanta
// collapse into a single resume.
//
// Scans entries via the cached IChatsBackend.GetTile, rolling LastScannedLid
// forward each run. The cursor stops at the first streaming-but-not-yet-stale
// entry — subsequent runs resume from there, so steady-state cost is a few
// tiles per kick instead of a full table scan.
//
// A run must fit into QueuesExt.QueueTimeout: a timed-out Resume commits
// nothing, so the cursor stays put and the next run repeats the same work
// forever. Hence MaxTilesPerRun/MaxRunDuration - on overrun the run commits
// what it scanned and stages an immediate self-resume to continue. ScanLid
// carries the sweep position across those resumes: LastScannedLid can't,
// since it stays pinned to the first entry that may still need fixing.
[Flow(DelayQuanta = 30)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class ChatEntryFixupFlow : Flow<Unit>
{
    private const int MaxTilesPerRun = 500;
    private static readonly TileLayer<long> IdTileLayer = Constants.Chat.ServerIdTileStack.FirstLayer;
    private static readonly TimeSpan MaxRunDuration = TimeSpan.FromSeconds(7);

    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private ICommander Commander => field ??= Services.Commander();

    private ChatId ChatId => field ??= ChatId.Parse(Id.Arguments);

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public Moment LastRunAt { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public long LastScannedLid { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public long TotalClosed { get; set; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public long TotalRemoved { get; set; }
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public long ScanLid { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var now = Hub.Clocks.SystemClock.Now;
        var staleBefore = now - Constants.Chat.StreamingEntryFixupDelay;
        LastRunAt = now;

        var lidRange = await ChatsBackend.GetLidRange(ChatId, false, cancellationToken).ConfigureAwait(false);
        var cursorLid = Math.Max(LastScannedLid, lidRange.Start);
        var startLid = Math.Max(ScanLid, cursorLid);
        if (startLid >= lidRange.End) {
            ScanLid = 0;
            return;
        }

        var closedCount = 0;
        var removedCount = 0;
        var cursor = cursorLid;
        // Mid-sweep runs resume past an already pinned cursor
        var isCursorAdvancing = startLid == cursorLid;
        var tilesScanned = 0;
        var isIncomplete = false;
        var scanLid = startLid;
        for (var idTile = IdTileLayer.GetTile(startLid); idTile.Start < lidRange.End; idTile = idTile.Next()) {
            var isOverBudget = tilesScanned > 0 && Hub.SystemNow - ResumedAt >= MaxRunDuration;
            if (tilesScanned >= MaxTilesPerRun || isOverBudget) {
                isIncomplete = true;
                break;
            }

            tilesScanned++;
            scanLid = Math.Min(idTile.End, lidRange.End);
            var tile = await ChatsBackend.GetTile(ChatId, idTile.Range, false, cancellationToken).ConfigureAwait(false);
            foreach (var entry in tile.Entries) {
                if (entry.LocalId < startLid)
                    continue;

                if (!entry.IsContentStreaming) {
                    if (isCursorAdvancing)
                        cursor = entry.LocalId + 1;
                    continue;
                }

                if (entry.BeginsAt >= staleBefore) {
                    // Streaming, not yet stale - we'll revisit it on a future kick.
                    isCursorAdvancing = false;
                    continue;
                }

                // Streaming and stale - fix it.
                try {
                    var change = entry.Content.IsNullOrEmpty()
                        ? Change.Remove<ChatEntryDiff>()
                        : Change.Update(new ChatEntryDiff {
                            ContentStreamId = "",
                            EndsAt = now,
                        });
                    await Commander.Call(new ChatsBackend_ChangeEntry(entry.Id, null, change), true, cancellationToken)
                        .ConfigureAwait(false);
                    if (entry.Content.IsNullOrEmpty())
                        removedCount++;
                    else
                        closedCount++;
                    if (isCursorAdvancing)
                        cursor = entry.LocalId + 1;
                }
                catch (Exception e) {
                    Console.LogError($"Failed to fix-up entry #{entry.Id}: {e.Message}", e);
                    isCursorAdvancing = false;
                }
            }

            // Skips gaps the per-entry update can't
            if (isCursorAdvancing)
                cursor = scanLid;
        }

        LastScannedLid = cursor;
        ScanLid = isIncomplete ? scanLid : 0;
        TotalClosed += closedCount;
        TotalRemoved += removedCount;
        if (closedCount > 0 || removedCount > 0)
            Console.Log($"Fix-up done: closed {closedCount}, removed {removedCount}; "
                + $"cursor {cursor}; totals {TotalClosed}/{TotalRemoved}");

        if (isIncomplete) {
            Console.Log($"Scanned {tilesScanned} tiles up to {scanLid} of {lidRange.End}, cursor {cursor} - resuming");
            Runtime.StageResume();
        }
    }
}
