using ActualLab.Diagnostics;

namespace ActualChat.Transcription;

/// <summary>
/// Periodically deletes Soniox files that <see cref="SonioxCleaner"/> never got to - its queue
/// lives in memory, so every id it holds is lost when a host dies mid-transcription, and Soniox
/// caps stored files per organization.
/// </summary>
public sealed class SonioxSweeper : WorkerBase
{
    public sealed record Options
    {
        // Hosts stagger themselves rather than coordinate: the first sweep lands anywhere in a
        // period, and each next one jitters, so N hosts don't converge on the same instant.
        public RandomTimeSpan Period { get; init; } = TimeSpan.FromHours(4).ToRandom(0.25);
        public RandomTimeSpan FirstDelay { get; init; } = new(TimeSpan.FromHours(2), TimeSpan.FromHours(2));
        // Must outlive the slowest in-flight offline transcription on any host: a file still being
        // transcribed elsewhere looks exactly like an orphan from here.
        public TimeSpan Retention { get; init; } = TimeSpan.FromMinutes(15);
        public int PageSize { get; init; } = 1000;
        public int MaxDeletesPerSweep { get; init; } = 5000;
        public RetryDelaySeq RetryDelays { get; init; }
            = RetryDelaySeq.Exp(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));
        public LogLevel LogLevel { get; init; } = LogLevel.Information;
    }

    private Options Settings { get; }
    private SonioxClient Client { get; }
    private MomentClock SystemClock { get; }
    private ILogger Log { get; }
    private ILogger? DefaultLog => Log.IfEnabled(Settings.LogLevel);

    public SonioxSweeper(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Client = services.GetRequiredService<SonioxClient>();
        SystemClock = services.Clocks().SystemClock;
        Log = services.LogFor(GetType());
    }

    // Protected methods

    protected override Task OnRun(CancellationToken cancellationToken)
        // PrependDelay wraps the cycling chain, so it staggers the host once rather than every cycle
        => AsyncChain.From(Cycle)
            .Log(LogLevel.Debug, Log)
            .RetryForever(Settings.RetryDelays, SystemClock, Log)
            .CycleForever()
            .PrependDelay(Settings.FirstDelay, SystemClock)
            .Run(cancellationToken);

    // It's internal so tests can run a single pass without the loop's delay
    internal async Task<int> Sweep(CancellationToken cancellationToken)
    {
        var deleted = 0;
        var kept = 0;
        var failed = 0;
        var isTruncated = false;
        var deleteBefore = SystemClock.Now - Settings.Retention;
        var cursor = (string?)null;
        do {
            var page = await Client
                .ListFiles(cursor, Settings.PageSize, cancellationToken)
                .ConfigureAwait(false);
            cursor = page.NextPageCursor;
            foreach (var file in page.Files ?? []) {
                // No timestamp means no way to tell an orphan from a live upload, so it stays.
                if (file.CreatedAt is not { } createdAt || (Moment)createdAt.UtcDateTime >= deleteBefore) {
                    kept++;
                    continue;
                }
                if (deleted + failed >= Settings.MaxDeletesPerSweep) {
                    isTruncated = true;
                    break;
                }

                try {
                    await Client.DeleteFile(file.Id, cancellationToken).ConfigureAwait(false);
                    deleted++;
                }
                catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                    // One bad id must not cost the rest of the sweep - the next run retries it.
                    failed++;
                    Log.LogWarning(e, "Sweep: couldn't delete file {FileId}", file.Id);
                }
            }
        } while (!cursor.IsNullOrEmpty() && !isTruncated && !cancellationToken.IsCancellationRequested);

        if (isTruncated)
            Log.LogWarning(
                "Sweep: stopped at {MaxCount} deletes, more remain - the next sweep continues",
                Settings.MaxDeletesPerSweep);
        if (deleted > 0 || failed > 0)
            DefaultLog?.Log(Settings.LogLevel,
                "Sweep: deleted {DeletedCount} orphaned file(s), kept {KeptCount}, {FailedCount} failed",
                deleted, kept, failed);
        return deleted;
    }

    // Private methods

    private async Task Cycle(CancellationToken cancellationToken)
    {
        await Sweep(cancellationToken).ConfigureAwait(false);
        await SystemClock.Delay(Settings.Period.Next(), cancellationToken).ConfigureAwait(false);
    }
}
