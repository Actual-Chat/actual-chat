namespace ActualChat.Transcription;

public static class TranscriptStreamExt
{
    public static Task<Transcript> GetTranscript(
        this IAsyncEnumerable<TranscriptUpdate> transcriptStream,
        CancellationToken cancellationToken)
    {
        var transcript = new Transcript();
        return transcript.WithUpdates(transcriptStream, cancellationToken);
    }
}
