using ActualLab.Diagnostics;

namespace ActualChat.Transcription;

/// <summary>
/// Periodically deletes the transcriptions and files that <see cref="SonioxCleaner"/> never got to -
/// its queue lives in memory, so every id it holds is lost when a host dies mid-transcription, and
/// Soniox caps both of them.
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
        // Transcriptions go first: deleting one takes its file with it, which leaves the file pass
        // with just the uploads whose transcription was never created - the shape a sweep sees once
        // the transcription cap is hit, since the upload succeeds and the create right after it 429s.
        var (deletedCount, failedCount) = await SweepPages(
                "transcription",
                ListTranscriptionPage,
                Client.DeleteTranscription,
                Settings.MaxDeletesPerSweep,
                cancellationToken)
            .ConfigureAwait(false);
        var (deletedFileCount, _) = await SweepPages(
                "file",
                ListFilePage,
                Client.DeleteFile,
                Settings.MaxDeletesPerSweep - deletedCount - failedCount,
                cancellationToken)
            .ConfigureAwait(false);
        return deletedCount + deletedFileCount;
    }

    // Private methods

    private async Task Cycle(CancellationToken cancellationToken)
    {
        await Sweep(cancellationToken).ConfigureAwait(false);
        await SystemClock.Delay(Settings.Period.Next(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<(int DeletedCount, int FailedCount)> SweepPages(
        string kind,
        Func<string?, CancellationToken, Task<SweepPage>> listPage,
        Func<string, CancellationToken, Task> delete,
        int maxDeleteCount,
        CancellationToken cancellationToken)
    {
        var deleted = 0;
        var kept = 0;
        var failed = 0;
        var isTruncated = false;
        var deleteBefore = SystemClock.Now - Settings.Retention;
        var cursor = (string?)null;
        do {
            var page = await listPage(cursor, cancellationToken).ConfigureAwait(false);
            cursor = page.NextPageCursor;
            foreach (var item in page.Items) {
                // No timestamp means no way to tell an orphan from a live upload, so it stays - and
                // so does anything Soniox is still working on, which it refuses to delete anyway.
                if (!item.IsDeletable
                    || item.CreatedAt is not { } createdAt
                    || (Moment)createdAt.UtcDateTime >= deleteBefore) {
                    kept++;
                    continue;
                }
                if (deleted + failed >= maxDeleteCount) {
                    isTruncated = true;
                    break;
                }

                try {
                    await delete(item.Id, cancellationToken).ConfigureAwait(false);
                    deleted++;
                }
                catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                    // One bad id must not cost the rest of the sweep - the next run retries it.
                    failed++;
                    Log.LogWarning(e, "Sweep: couldn't delete {Kind} {Id}", kind, item.Id);
                }
            }
        } while (!cursor.IsNullOrEmpty() && !isTruncated && !cancellationToken.IsCancellationRequested);

        if (isTruncated)
            Log.LogWarning(
                "Sweep: stopped at {MaxCount} {Kind} delete(s), more remain - the next sweep continues",
                maxDeleteCount, kind);
        if (deleted > 0 || failed > 0)
            DefaultLog?.Log(Settings.LogLevel,
                "Sweep: deleted {DeletedCount} orphaned {Kind}(s), kept {KeptCount}, {FailedCount} failed",
                deleted, kind, kept, failed);
        return (deleted, failed);
    }

    private async Task<SweepPage> ListTranscriptionPage(string? cursor, CancellationToken cancellationToken)
    {
        var page = await Client
            .ListTranscriptions(cursor, Settings.PageSize, cancellationToken)
            .ConfigureAwait(false);
        var items = (page.Transcriptions ?? [])
            .Select(x => new SweepItem(x.Id, x.CreatedAt, x.Status is "completed" or "error"))
            .ToArray();
        return new SweepPage(items, page.NextPageCursor);
    }

    private async Task<SweepPage> ListFilePage(string? cursor, CancellationToken cancellationToken)
    {
        var page = await Client
            .ListFiles(cursor, Settings.PageSize, cancellationToken)
            .ConfigureAwait(false);
        var items = (page.Files ?? [])
            .Select(x => new SweepItem(x.Id, x.CreatedAt, true))
            .ToArray();
        return new SweepPage(items, page.NextPageCursor);
    }

    // Nested types

    private readonly record struct SweepItem(string Id, DateTimeOffset? CreatedAt, bool IsDeletable);
    private readonly record struct SweepPage(SweepItem[] Items, string? NextPageCursor);
}
