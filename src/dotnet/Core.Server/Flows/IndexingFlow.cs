using MemoryPack;

namespace ActualChat.Flows;

// Base class for flows that perform cursor-based indexing operations.
// Processes data in batches with support for recheck and watchdog intervals.
public abstract class IndexingFlow<TCursor> : Flow<string>, IHasLastRunAt
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan MaxResumeDelay { get; } = TimeSpan.FromDays(1);

    // Persisted state
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public TCursor? Cursor { get; protected set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public Moment LastRunAt { get; protected set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public FlowReadiness LastReadiness { get; protected set; }

    // Overridable methods

    protected virtual ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken) => new(FlowReadiness.Ready);
    protected abstract ValueTask<BatchIndexingResult<TCursor>> Process(TCursor? cursor, CancellationToken cancellationToken);

    // Implementation

    protected override ValueTask Init(CancellationToken cancellationToken)
    {
        Cursor = default;
        return default;
    }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        LastReadiness = await Prepare(cancellationToken).ConfigureAwait(false);
        if (LastReadiness is { IsSuspended: true } readiness) {
            var resumeDelay = ResumedAt + (readiness.ResumeDelay ?? MaxResumeDelay);
            Console.Log($"Prepare() -> {readiness}, will resume at {resumeDelay}");
            Runtime.StageResumeAt(resumeDelay);
            return;
        }

        LastRunAt = ResumedAt;
        Console.Log($"Process({Cursor})");
        var result = await Process(Cursor, cancellationToken).ConfigureAwait(false);
        Cursor = result.Cursor;

        Console.Log($"Process(...) -> {result}");
        if (!result.CompletionReason.IsNullOrEmpty())
            await Complete(result.CompletionReason, cancellationToken).ConfigureAwait(false);
        else if (result.IsTailReached)
            await TailReached(result.HasProcessedAnyItems, cancellationToken).ConfigureAwait(false);
        else {
            Console.Log("Scheduling immediate resume");
            Runtime.StageResume();
        }
    }

    protected virtual ValueTask TailReached(bool hasProcessedAnyItems, CancellationToken cancellationToken)
    {
        Console.Log($"Tail reached, scheduling resume in {MaxResumeDelay.ToShortString()}");
        Runtime.StageResumeIn(MaxResumeDelay);
        return default;
    }

    protected virtual ValueTask Complete(string completionReason, CancellationToken cancellationToken)
    {
        SetResult(completionReason);
        return default;
    }
}
