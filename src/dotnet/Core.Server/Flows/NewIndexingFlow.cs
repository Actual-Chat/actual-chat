using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Flows;

/// <summary>
/// Base class for cursor-based indexing flows.
/// Subclasses implement <see cref="ProcessNextBatch"/> to define the indexing logic.
/// The flow handles scheduling, reindexing detection, and resume timing automatically.
/// </summary>
/// <typeparam name="TCursor">Type of the cursor tracking indexing progress.</typeparam>
public abstract class NewIndexingFlow<TCursor> : Flow<Unit>
{
    // ═══════════════════════════════════════════════════════════════════
    // Persisted State
    // ═══════════════════════════════════════════════════════════════════

    [DataMember(Order = 100), MemoryPackOrder(100)]
    public TCursor? Cursor { get; protected set; }

    [DataMember(Order = 101), MemoryPackOrder(101)]
    public Moment LastRunAt { get; protected set; }

    [DataMember(Order = 102), MemoryPackOrder(102)]
    public int FlowSetVersion { get; protected set; }

    // ═══════════════════════════════════════════════════════════════════
    // Configuration (override in subclasses)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Current flow set version. Bump this to force reindexing.
    /// When FlowSetVersion &lt; CurrentFlowSetVersion, the cursor is reset.
    /// </summary>
    [IgnoreDataMember, MemoryPackIgnore]
    protected abstract int CurrentFlowSetVersion { get; }

    /// <summary>Maximum batches to process per Resume() call before committing.</summary>
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual int MaxBatchesPerResume { get; } = 10;

    /// <summary>How often to check for new items when tail is reached and items were processed.</summary>
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan RecheckInterval { get; } = TimeSpan.FromSeconds(10);

    /// <summary>Watchdog timer interval when tail is reached and nothing was processed.</summary>
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan WatchdogInterval { get; } = TimeSpan.FromHours(24);

    /// <summary>Timer quantization for resume scheduling.</summary>
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan TimerDelayQuanta { get; } = TimeSpan.FromSeconds(1);

    // ═══════════════════════════════════════════════════════════════════
    // Abstract Methods - Subclasses Must Implement
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Process the next batch starting from current cursor position.
    /// Returns information about what was processed and where to continue.
    /// </summary>
    /// <param name="cursor">Current cursor position (null = start from beginning).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="IndexingBatch{TCursor}"/> indicating:
    /// - IsEmpty: whether any items were found
    /// - IsTailReached: whether we've caught up with the data source
    /// - NextCursor: updated cursor position
    /// </returns>
    protected abstract Task<IndexingBatch<TCursor>> ProcessNextBatch(
        TCursor? cursor,
        CancellationToken cancellationToken);

    // ═══════════════════════════════════════════════════════════════════
    // Optional Hooks
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called before first indexing run (on start or after reset/reindex).
    /// Return false to suspend the flow (awaits external resume).
    /// </summary>
    protected virtual Task<bool> OnBeforeIndex(CancellationToken cancellationToken)
        => Task.FromResult(true);

    /// <summary>
    /// Called when tail is reached. Can perform cleanup, request index refreshes, etc.
    /// </summary>
    protected virtual Task OnTailReached(bool hasProcessedAny, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Decides whether to use RecheckInterval (true) or WatchdogInterval (false) after tail is reached.
    /// Default: recheck if we processed anything, watchdog if idle.
    /// </summary>
    protected virtual bool ShouldRecheck(bool hasProcessedAny)
        => hasProcessedAny;

    // ═══════════════════════════════════════════════════════════════════
    // Core Resume Logic
    // ═══════════════════════════════════════════════════════════════════

    protected sealed override async ValueTask Resume(CancellationToken cancellationToken)
    {
        Runtime.DefaultResumeDelayQuanta = TimerDelayQuanta;
        var needsReindex = FlowSetVersion < CurrentFlowSetVersion;

        // Handle initialization or reindex
        if (needsReindex) {
            Cursor = default;
            FlowSetVersion = CurrentFlowSetVersion;
            Console.Log($"Reset cursor for reindex (v{FlowSetVersion})");
            if (!await OnBeforeIndex(cancellationToken).ConfigureAwait(false)) {
                Console.Log("Suspended by OnBeforeIndex");
                return; // No resume scheduled - awaits external trigger
            }
        }

        LastRunAt = Runtime.Now;

        // Process batches until quota exhausted or tail reached
        var batchCount = 0;
        var hasProcessedAny = false;
        IndexingBatch<TCursor> batch;

        do {
            batch = await ProcessNextBatch(Cursor, cancellationToken).ConfigureAwait(false);
            if (!batch.IsEmpty) {
                hasProcessedAny = true;
                Cursor = batch.NextCursor;
            }
            batchCount++;
        } while (!batch.IsEmpty && !batch.IsTailReached && batchCount < MaxBatchesPerResume);

        Console.Log($"Processed {batchCount} batches, hasProcessedAny={hasProcessedAny}, tailReached={batch.IsTailReached}");

        // Schedule next resume
        if (!batch.IsTailReached) {
            Runtime.ScheduleResume(); // More work immediately
            return;
        }

        await OnTailReached(hasProcessedAny, cancellationToken).ConfigureAwait(false);

        var delay = ShouldRecheck(hasProcessedAny) ? RecheckInterval : WatchdogInterval;
        Runtime.ScheduleResumeIn(delay);
        Console.Log($"Tail reached, will resume in {delay.ToShortString()}");
    }
}
