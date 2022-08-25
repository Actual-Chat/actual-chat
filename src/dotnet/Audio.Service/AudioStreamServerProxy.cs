namespace ActualChat.Audio;

public class AudioStreamServerProxy : IAudioStreamServer
{
    private AudioStreamServer AudioStreamServer { get; }
    private AudioHubBackendClient AudioHubBackendClient { get; }

    public AudioStreamServerProxy(AudioStreamServer audioStreamServer, AudioHubBackendClient audioHubBackendClient)
    {
        AudioStreamServer = audioStreamServer;
        AudioHubBackendClient = audioHubBackendClient;
    }

    public Task<Option<IAsyncEnumerable<byte[]>>> Read(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken)
        => AudioHubBackendClient.Read(streamId, skipTo, cancellationToken);

    public Task Write(Symbol streamId, IAsyncEnumerable<byte[]> audioStream, CancellationToken cancellationToken)
        => AudioHubBackendClient.Write(streamId, audioStream, cancellationToken);
}
