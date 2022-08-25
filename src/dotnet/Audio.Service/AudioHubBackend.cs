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

    public async IAsyncEnumerable<byte[]> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (hasValue, audioStream) = await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        if (!hasValue)
            yield break;

        await foreach (var chunk in audioStream!.ConfigureAwait(false))
            yield return chunk;
    }

    public async IAsyncEnumerable<Transcript> GetTranscriptStream(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (hasValue, transcriptStream) = await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        if (!hasValue)
            yield break;

        await foreach (var chunk in transcriptStream!.ConfigureAwait(false))
            yield return chunk;
    }

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
