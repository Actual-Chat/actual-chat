using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Flows;

/// <summary>
/// Base class for flows that execute a task periodically.
/// Subclasses implement <see cref="Run"/> and <see cref="ComputeNextRunAt"/> to define the behavior.
/// </summary>
public abstract class NewPeriodicFlow : Flow<Unit>
{
    // ═══════════════════════════════════════════════════════════════════
    // Persisted State
    // ═══════════════════════════════════════════════════════════════════

    [DataMember(Order = 100), MemoryPackOrder(100)]
    public Moment LastRunAt { get; protected set; }

    [DataMember(Order = 101), MemoryPackOrder(101)]
    public Moment? NextRunAt { get; protected set; }

    [DataMember(Order = 102), MemoryPackOrder(102)]
    public int RunCount { get; protected set; }

    // ═══════════════════════════════════════════════════════════════════
    // Configuration (override in subclasses)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Maximum delay between runs (caps the result of <see cref="ComputeNextRunAt"/>).</summary>
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan MaxDelay { get; } = TimeSpan.FromDays(7);

    /// <summary>Timer quantization for resume scheduling.</summary>
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan TimerDelayQuanta { get; } = TimeSpan.FromSeconds(1);

    // ═══════════════════════════════════════════════════════════════════
    // Abstract Methods - Subclasses Must Implement
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Execute the periodic task.</summary>
    protected abstract Task Run(CancellationToken cancellationToken);

    /// <summary>Compute when to run next. The result is clamped to [now, now + MaxDelay].</summary>
    protected abstract Moment ComputeNextRunAt(Moment now);

    // ═══════════════════════════════════════════════════════════════════
    // Optional Hooks
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called before each run. Return null to proceed, or a reason string to end the flow.
    /// </summary>
    protected virtual Task<string?> OnBeforeRun(CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    // ═══════════════════════════════════════════════════════════════════
    // Core Resume Logic
    // ═══════════════════════════════════════════════════════════════════

    protected sealed override async ValueTask Resume(CancellationToken cancellationToken)
    {
        Runtime.DefaultResumeDelayQuanta = TimerDelayQuanta;
        var now = Runtime.Now;

        // Check if we should end
        var endReason = await OnBeforeRun(cancellationToken).ConfigureAwait(false);
        if (endReason is not null) {
            Console.Log($"Ending: {endReason}");
            SetResult(default);
            return;
        }

        // Check if it's time to run
        if (NextRunAt is null || now >= NextRunAt) {
            await Run(cancellationToken).ConfigureAwait(false);
            LastRunAt = now;
            RunCount++;
            Console.Log($"Run #{RunCount} completed");
            NextRunAt = null; // Will be recomputed below
        }

        // Schedule next run
        var nextRunAt = ComputeNextRunAt(now);
        nextRunAt = now + (nextRunAt - now).Clamp(TimeSpan.Zero, MaxDelay);
        NextRunAt = nextRunAt;
        Runtime.ScheduleResumeAt(nextRunAt);
        Console.Log($"Next run at {nextRunAt}");
    }
}
