using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR;

namespace ActualChat.Audio;

public class AudioHubBackend : Hub
{
    private AudioStreamServer AudioStreamServer { get; }
    private TranscriptStreamServer TranscriptStreamServer { get; }

    public AudioHubBackend(
        AudioStreamServer audioStreamServer,
        TranscriptStreamServer transcriptStreamServer)
    {
        AudioStreamServer = audioStreamServer;
        TranscriptStreamServer = transcriptStreamServer;
    }

    public async IAsyncEnumerable<byte[]> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in stream.ConfigureAwait(false))
            yield return chunk;
    }

    public async IAsyncEnumerable<TranscriptDiff> GetTranscriptDiffStream(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in stream!.ConfigureAwait(false))
            yield return chunk;
    }

    public async Task WriteAudioStream(
        string streamId,
        IAsyncEnumerable<byte[]> stream) // No CancellationToken argument here, otherwise SignalR binder fails!
    {
        var cancellationToken = Context.GetHttpContext()!.RequestAborted;
        await AudioStreamServer.Write(streamId, stream, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteTranscriptDiffStream(
        string streamId,
        IAsyncEnumerable<TranscriptDiff> stream) // No CancellationToken argument here, otherwise SignalR binder fails!
    {
        var cancellationToken = Context.GetHttpContext()!.RequestAborted;
        await TranscriptStreamServer.Write(streamId, stream, cancellationToken).ConfigureAwait(false);
    }
}
