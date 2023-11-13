using ActualChat.Transcription;

namespace ActualChat.Audio;

public class TranscriptStreamer(
    ITranscriptStreamServer transcriptStreamServer,
    ILogger<TranscriptStreamer> log
    ) : ITranscriptStreamer
{
    private ITranscriptStreamServer TranscriptStreamServer { get; } = transcriptStreamServer;
    private ILogger<TranscriptStreamer> Log { get; } = log;

    public async IAsyncEnumerable<TranscriptDiff> GetTranscriptDiffStream(
        Symbol streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var transcriptStream = await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
        await foreach(var transcript in transcriptStream.ConfigureAwait(false))
            yield return transcript;
    }
}
