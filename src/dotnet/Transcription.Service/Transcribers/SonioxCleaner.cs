namespace ActualChat.Transcription;

/// <summary>
/// Deletes what <see cref="SonioxOfflineTranscriber"/> leaves on Soniox - the organization has a
/// hard cap on the number of stored files. Ids are queued as each transcription ends, so the two
/// deletes stay off its latency path, and the queue is drained on shutdown.
/// </summary>
public sealed class SonioxCleaner : SafeAsyncDisposableBase
{
    public sealed record Options
    {
        public TimeSpan FlushDelay { get; init; } = TimeSpan.FromMilliseconds(100);
        public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(10);
        public RetryDelaySeq FlushRetryDelays { get; init; } = RetryDelaySeq.Exp(1, 60);
    }

    private SonioxClient Client { get; }
    private LazyWriter<(string? TranscriptionId, string? FileId)> Writer { get; }
    private ILogger Log { get; }

    public SonioxCleaner(Options settings, IServiceProvider services)
    {
        Client = services.GetRequiredService<SonioxClient>();
        Log = services.LogFor(GetType());
        Writer = new LazyWriter<(string? TranscriptionId, string? FileId)>() {
            FlushDelay = settings.FlushDelay,
            DisposeTimeout = settings.DrainTimeout,
            FlushRetryDelays = settings.FlushRetryDelays,
            Implementation = DeleteBatch,
            FlushErrorSeverityProvider = static _ => LogLevel.Warning,
            Log = Log,
        };
    }

    // Enqueue may still be called while the queue drains, so it tolerates an already disposed
    // Writer: a dropped id just leaves its artifacts on Soniox until the cap forces a manual sweep.
    public void Enqueue(string? transcriptionId, string? fileId)
    {
        if (transcriptionId == null && fileId == null)
            return;

        try {
            Writer.Add((transcriptionId, fileId));
        }
        catch (Exception e) {
            Log.LogWarning(e,
                "Enqueue failed, leaving transcription {TranscriptionId} and file {FileId} on Soniox",
                transcriptionId, fileId);
        }
    }

    public Task Flush(CancellationToken cancellationToken = default)
        => Writer.Flush(cancellationToken);

    // Protected methods

    protected override Task DisposeAsync(bool disposing)
        => Writer.DisposeAsync().AsTask();

    // Private methods

    private async Task DeleteBatch(List<(string? TranscriptionId, string? FileId)> batch)
    {
        // A throw hands the batch back to the Writer's retry, which is the only retry these deletes
        // get - so both must be idempotent, and they are: a repeat gets 404, which counts as success.
        // Soniox deletes the transcription's file along with it, hence the file delete second.
        foreach (var (transcriptionId, fileId) in batch) {
            if (transcriptionId != null)
                await Client.DeleteTranscription(transcriptionId, CancellationToken.None).ConfigureAwait(false);
            if (fileId != null)
                await Client.DeleteFile(fileId, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
