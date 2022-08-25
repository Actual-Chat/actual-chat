using ActualChat.Transcription;

namespace ActualChat.Audio;

public class TranscriptStreamer : ITranscriptStreamer
{
    private ITranscriptStreamServer TranscriptStreamServer { get; }
    private ILogger<TranscriptStreamer> Log { get; }

    public TranscriptStreamer(
        ITranscriptStreamServer transcriptStreamServer,
        ILogger<TranscriptStreamer> log)
    {
        TranscriptStreamServer = transcriptStreamServer;
        Log = log;
    }

    public async IAsyncEnumerable<Transcript> GetTranscriptDiffStream(
        Symbol streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var transcriptStreamOption = await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        if (!transcriptStreamOption.HasValue)
            Log.LogWarning("{TranscriptStreamServer} doesn't have transcript stream", TranscriptStreamServer.GetType().Name);
        var transcriptStream = transcriptStreamOption.HasValue
            ? transcriptStreamOption.Value
            : AsyncEnumerable.Empty<Transcript>();
        await foreach(var transcript in transcriptStream.ConfigureAwait(false))
            yield return transcript;
    }
}
