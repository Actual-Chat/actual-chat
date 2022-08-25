using ActualChat.Transcription;

namespace ActualChat.Audio;

public class TranscriptStreamServerProxy : ITranscriptStreamServer
{
    private TranscriptStreamServer TranscriptStreamServer { get; }
    private AudioHubBackendClient AudioHubBackendClient { get; }

    public TranscriptStreamServerProxy(TranscriptStreamServer transcriptStreamServer, AudioHubBackendClient audioHubBackendClient)
    {
        TranscriptStreamServer = transcriptStreamServer;
        AudioHubBackendClient = audioHubBackendClient;
    }

    public IAsyncEnumerable<Transcript> Read(Symbol streamId, CancellationToken cancellationToken)
        => AudioHubBackendClient.Read(streamId, cancellationToken);

    public Task Write(Symbol streamId, IAsyncEnumerable<Transcript> transcriptStream, CancellationToken cancellationToken)
        => AudioHubBackendClient.Write(streamId, transcriptStream, cancellationToken);
}
