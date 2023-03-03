namespace ActualChat.Transcription;

public static class TranscriptStreamExt
{
    public static async IAsyncEnumerable<Transcript> GetDiffs(
        this IAsyncEnumerable<Transcript> transcripts,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lastTranscript = new Transcript();
        await foreach (var transcript in transcripts.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var diff = transcript.DiffWith(lastTranscript);
            lastTranscript = transcript;
            if (diff.Length == 0)
                continue;

            yield return diff;
        }
    }

    public static async IAsyncEnumerable<Transcript> ApplyDiffs(
        this IAsyncEnumerable<Transcript> diffs,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Transcript? transcript = null;
        await foreach (var diff in diffs.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            transcript = transcript == null ? diff : transcript.WithDiff(diff);
            yield return transcript;
        }
    }

    public static IEnumerable<Transcript> ApplyDiffs(this IEnumerable<Transcript> diffs)
    {
        Transcript? transcript = null;
        foreach (var diff in diffs) {
            transcript = transcript == null ? diff : transcript.WithDiff(diff);
            yield return transcript;
        }
    }
}
