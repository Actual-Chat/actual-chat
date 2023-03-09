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

    public async IAsyncEnumerable<TranscriptDiff> GetTranscriptDiffStream(
        Symbol streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var transcriptStream = await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        await foreach(var transcript in transcriptStream.ConfigureAwait(false))
            yield return transcript;
    }
}
