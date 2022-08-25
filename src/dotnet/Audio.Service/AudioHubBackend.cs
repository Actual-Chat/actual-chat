using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR;

namespace ActualChat.Audio;

public class AudioHubBackend : Hub
{
    private AudioStreamServer AudioStreamServer { get; }
    private TranscriptStreamServer TranscriptStreamServer { get; }

    public AudioHubBackend(AudioStreamServer audioStreamServer, TranscriptStreamServer transcriptStreamServer)
    {
        AudioStreamServer = audioStreamServer;
        TranscriptStreamServer = transcriptStreamServer;
    }

    public IAsyncEnumerable<byte[]>? GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
        => AudioStreamServer.Read(streamId, skipTo, cancellationToken);

    public IAsyncEnumerable<Transcript>? GetTranscriptStream(
        string streamId,
        CancellationToken cancellationToken)
        => TranscriptStreamServer.Read(streamId, cancellationToken);

    public Task WriteAudioStream(
        string streamId,
        IAsyncEnumerable<byte[]> audioStream)
        // No CancellationToken argument here, otherwise SignalR binder fails!
        => AudioStreamServer.Write(streamId, audioStream, Context.GetHttpContext()!.RequestAborted);

    public Task WriteTranscriptStream(
        string streamId,
        IAsyncEnumerable<Transcript> transcriptStream)
        // No CancellationToken argument here, otherwise SignalR binder fails!
        => TranscriptStreamServer.Write(streamId, transcriptStream, Context.GetHttpContext()!.RequestAborted);

}
