namespace ActualChat.Transcription;

public static class TranscriptStreamExt
{
    public static async IAsyncEnumerable<TranscriptDiff> ToTranscriptDiffs(this IAsyncEnumerable<Transcript> transcripts)
    {
        var lastTranscript = Transcript.Empty;
        await foreach (var transcript in transcripts.ConfigureAwait(false)) {
            var diff = transcript - lastTranscript;
            lastTranscript = transcript;
            if (diff.IsNone)
                continue;

            yield return diff;
        }
    }

    public static IEnumerable<TranscriptDiff> ToTranscriptDiffs(this IEnumerable<Transcript> transcripts)
    {
        var lastTranscript = Transcript.Empty;
        foreach (var transcript in transcripts) {
            var diff = transcript - lastTranscript;
            lastTranscript = transcript;
            if (diff.IsNone)
                continue;

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
}
