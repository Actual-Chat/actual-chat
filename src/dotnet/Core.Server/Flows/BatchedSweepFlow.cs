namespace ActualChat.Flows;

/// <summary>
/// Base class for flows that sweep a set in batches on request. An external resume starts a sweep,
/// or, while one is in progress, makes it sweep once more after it ends. Batches chain through the
/// flow's own resumes, which it tells from external ones by <see cref="FlowResumeEvent.IsSelfResume"/>.
/// </summary>
public abstract class BatchedSweepFlow : Flow<string>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    protected virtual TimeSpan StallTimeout
        // An external resume that finds a sweep this late on its next batch resumes the sweep itself
        => TimeSpan.FromMinutes(5);

    // Persisted state

    [DataMember(Order = 0), Key(0)]
    public bool IsSweeping { get; set; }
    [DataMember(Order = 1), Key(1)]
    public bool MustSweepAgain { get; set; }
    [DataMember(Order = 2), Key(2)]
    public Moment NextBatchAt { get; set; }
    [DataMember(Order = 3), Key(3)]
    public int SweepCount { get; set; }

    // Protected/internal methods

    // Resets whatever a sweep tracks, e.g. its cursor
    protected abstract ValueTask StartSweep(CancellationToken cancellationToken);
    // Returns when the next batch is due, or null once the sweep is done
    protected abstract ValueTask<Moment?> RunBatch(CancellationToken cancellationToken);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        if (!ResumeEvent.IsSelfResume) {
            if (!IsSweeping) {
                Console.Log($"Sweep #{SweepCount + 1} started");
                IsSweeping = true;
                MustSweepAgain = false;
                await StartSweep(cancellationToken).ConfigureAwait(false);
            }
            else if (ResumedAt < NextBatchAt + StallTimeout) {
                // The sweep's own resume is still pending, so this one only books the next sweep
                Console.Log("Sweep in progress, one more sweep requested");
                MustSweepAgain = true;
                return;
            }
            else
                Console.Log($"Sweep stalled since {NextBatchAt}, resuming it");
        }
        else if (!IsSweeping)
            return; // An own resume that outlived its sweep, e.g. after a stall

        var nextBatchAt = await RunBatch(cancellationToken).ConfigureAwait(false);
        if (nextBatchAt is null) {
            SweepCount++;
            Console.Log($"Sweep #{SweepCount} completed");
            IsSweeping = MustSweepAgain;
            MustSweepAgain = false;
            if (!IsSweeping)
                return;

            Console.Log($"Sweep #{SweepCount + 1} started");
            await StartSweep(cancellationToken).ConfigureAwait(false);
            nextBatchAt = Hub.SystemNow;
        }

        // Zero quanta keeps the pace: the flow's quanta would round each batch up to its slot
        NextBatchAt = Moment.Max(nextBatchAt.Value, Hub.SystemNow);
        Runtime.StageResumeAt(NextBatchAt, TimeSpan.Zero);
    }
}
