namespace ActualChat.Flows;

// Base class for flows that perform cursor-based indexing operations.
public abstract class IndexingFlow<TCursor> : Flow<string>, IHasLastRunAt
{
    // Persisted state
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public TCursor? Cursor { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public Moment LastRunAt { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public FlowReadiness LastReadiness { get; set; }

    // Overridable methods

    protected abstract ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken);
    protected abstract ValueTask<BatchIndexingResult<TCursor>> Run(TCursor? cursor, CancellationToken cancellationToken);

    // When > Zero, a non-tail Run schedules its continuation after this delay instead of
    // resuming immediately - used to throttle fan-out / backfill load. Quanta is forced to
    // Zero so consecutive self-resumes are never deduped into the same bucket.
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    protected virtual TimeSpan BackfillResumeDelay => TimeSpan.Zero;

    // Implementation

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        LastReadiness = await Prepare(cancellationToken).ConfigureAwait(false);
        if (LastReadiness is { IsSuspended: true } readiness) {
            if (readiness.ResumeDelay is null) {
                Console.Log($"Prepare() -> {readiness}, skipping resume");
                return;
            }

            var resumeDelay = ResumedAt + readiness.ResumeDelay.Value;
            Console.Log($"Prepare() -> {readiness}, will resume at {resumeDelay}");
            Runtime.StageResumeAt(resumeDelay);
            return;
        }

        LastRunAt = ResumedAt;
        Console.Log($"Run({Cursor})");
        var result = await Run(Cursor, cancellationToken).ConfigureAwait(false);
        Cursor = result.Cursor;

        Console.Log($"Run(...) -> {result}");
        if (!result.CompletionReason.IsNullOrEmpty())
            await Complete(result.CompletionReason, cancellationToken).ConfigureAwait(false);
        else if (result.IsTailReached)
            await TailReached(result.HasProcessedAnyItems, cancellationToken).ConfigureAwait(false);
        else if (BackfillResumeDelay > TimeSpan.Zero) {
            Console.Log($"Scheduling resume in {BackfillResumeDelay.ToShortString()}");
            Runtime.StageResumeIn(BackfillResumeDelay, TimeSpan.Zero);
        }
        else {
            Console.Log("Scheduling immediate resume");
            Runtime.StageResume();
        }
    }

    protected virtual ValueTask TailReached(bool hasProcessedAnyItems, CancellationToken cancellationToken)
    {
        Console.Log("Tail reached, will wait for resume events");
        return default;
    }

    protected virtual ValueTask Complete(string completionReason, CancellationToken cancellationToken)
    {
        SetResult(completionReason);
        return default;
    }
}
