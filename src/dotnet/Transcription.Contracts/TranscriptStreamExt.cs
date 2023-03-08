namespace ActualChat.Transcription;

public static class TranscriptStreamExt
{
    public static async IAsyncEnumerable<TranscriptDiff> GetDiffs(
        this IAsyncEnumerable<Transcript> transcripts,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lastTranscript = Transcript.Empty;
        await foreach (var transcript in transcripts.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var diff = transcript - lastTranscript;
            lastTranscript = transcript;
            if (diff.IsNone)
                continue;

            yield return diff;
        }
    }

    public static async IAsyncEnumerable<Transcript> ApplyDiffs(
        this IAsyncEnumerable<TranscriptDiff> diffs,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Transcript transcript = Transcript.Empty;
        await foreach (var diff in diffs.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            transcript += diff;
            yield return transcript;
        }
    }

    public static IEnumerable<Transcript> ApplyDiffs(this IEnumerable<TranscriptDiff> diffs)
    {
        Transcript transcript = Transcript.Empty;
        foreach (var diff in diffs) {
            transcript += diff;
            yield return transcript;
        }
    }
}
