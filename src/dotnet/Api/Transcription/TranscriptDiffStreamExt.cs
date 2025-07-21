namespace ActualChat.Transcription;

public static class TranscriptStreamExt
{
    public static async IAsyncEnumerable<TranscriptDiff> ToTranscriptDiffs(this IAsyncEnumerable<Transcript> transcripts)
    {
        var lastTranscript = Transcript.Empty;
        await foreach (var transcript in transcripts.ConfigureAwait(false)) {
            var diff = transcript - lastTranscript;
            lastTranscript = transcript;
            yield return diff;
        }
    }

    public static IEnumerable<TranscriptDiff> ToTranscriptDiffs(this IEnumerable<Transcript> transcripts)
    {
        var lastTranscript = Transcript.Empty;
        foreach (var transcript in transcripts) {
            var diff = transcript - lastTranscript;
            lastTranscript = transcript;
            yield return diff;
        }
    }

    public static IEnumerable<TranscriptDiff> ToTranscriptDiffs(this IEnumerable<StringDiff> stringDiffs)
        => stringDiffs.Select(stringDiff => new TranscriptDiff(stringDiff, LinearMapDiff.None) { IsStable = true });

    public static async IAsyncEnumerable<TranscriptDiff> ToTranscriptDiffs(this IAsyncEnumerable<StringDiff> stringDiffs)
    {
        await foreach (var stringDiff in stringDiffs.ConfigureAwait(false)) {
            var diff = new TranscriptDiff(stringDiff, LinearMapDiff.None);
            yield return diff;
        }
    }

    public static async IAsyncEnumerable<Transcript> ToTranscripts(this IAsyncEnumerable<TranscriptDiff> diffs)
    {
        var transcript = Transcript.Empty;
        await foreach (var diff in diffs.ConfigureAwait(false)) {
            transcript += diff;
            yield return transcript;
        }
    }

    public static IEnumerable<Transcript> ToTranscripts(this IEnumerable<TranscriptDiff> diffs)
    {
        var transcript = Transcript.Empty;
        foreach (var diff in diffs) {
            transcript += diff;
            yield return transcript;
        }
    }

    public static async IAsyncEnumerable<Transcript> ThrottleTranscript(
        this IAsyncEnumerable<Transcript> source,
        TimeSpan minInterval,
        MomentClock clock,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (minInterval <= TimeSpan.Zero)
            throw StandardError.NotSupported("Zero interval is not supported for throttling transcripts.");

        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        var hasNextTask = enumerator.MoveNextAsync().AsTask();
        Task? timerTask = null;
        var isFirstTranscript = true;
        Transcript? pendingTranscript = null;

        while (!cancellationToken.IsCancellationRequested) {
            if (timerTask?.IsCompleted != false) {
                // Timer ticked or not started yet
                if (pendingTranscript != null) {
                    yield return pendingTranscript;
                    ResetPendingState();
                }
                timerTask = null; // Reset timer task
            }
            if (hasNextTask.IsCompleted) {
                if (!await hasNextTask.ConfigureAwait(false))
                    yield break;

                var transcript = enumerator.Current;
                if (transcript.IsStable || isFirstTranscript) {
                    yield return transcript;
                    ResetPendingState();
                    timerTask = clock.Delay(minInterval, cancellationToken);
                }
                else {
                    pendingTranscript = transcript;
                    timerTask ??= clock.Delay(minInterval, cancellationToken);
                }
                hasNextTask = enumerator.MoveNextAsync().AsTask();
            }
            else {
                // Only convert to Task when awaiting is necessary
                var awaitableTimerTask = timerTask?.IsCompleted != false
                    ? hasNextTask
                    : timerTask;
                var completedTask = await Task.WhenAny(hasNextTask, awaitableTimerTask).ConfigureAwait(false);
                if (completedTask != hasNextTask)
                    // Timer has ticked
                    timerTask = null; // Reset timer task
            }
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (pendingTranscript != null)
            yield return pendingTranscript;

        yield break;

        void ResetPendingState()
        {
            pendingTranscript = null;
            isFirstTranscript = false;
        }
    }
}
