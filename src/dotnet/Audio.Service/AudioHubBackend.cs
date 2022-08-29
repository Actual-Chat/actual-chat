using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR;

namespace ActualChat.Audio;

public class AudioHubBackend : Hub
{
    private readonly Channel<Ack> _ackStream;

    private AudioStreamServer AudioStreamServer { get; }
    private TranscriptStreamServer TranscriptStreamServer { get; }

    public AudioHubBackend(AudioStreamServer audioStreamServer, TranscriptStreamServer transcriptStreamServer)
    {
        AudioStreamServer = audioStreamServer;
        TranscriptStreamServer = transcriptStreamServer;
        _ackStream = Channel.CreateBounded<Ack>(new BoundedChannelOptions(1000) {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    }

    public IAsyncEnumerable<Ack> ReadAckStream(CancellationToken cancellationToken)
        => _ackStream.Reader.ReadAllAsync(cancellationToken);


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

    public async Task WriteAudioStream(
        string streamId,
        IAsyncEnumerable<byte[]> audioStream) // No CancellationToken argument here, otherwise SignalR binder fails!
    {
        var cancellationToken = Context.GetHttpContext()!.RequestAborted;
        var completeTask = await AudioStreamServer.StartWrite(streamId, audioStream, cancellationToken).ConfigureAwait(false);
        await _ackStream.Writer
            .WriteAsync(new Ack(AckType.Received, StreamType.Audio, streamId), CancellationToken.None)
            .ConfigureAwait(false);

        _ = BackgroundTask.Run(async () => {
            await completeTask.ConfigureAwait(false);
            await _ackStream.Writer
                .WriteAsync(new Ack(AckType.Completed, StreamType.Audio, streamId), CancellationToken.None)
                .ConfigureAwait(false);
        }, CancellationToken.None);
    }

    public async Task WriteTranscriptStream(
        string streamId,
        IAsyncEnumerable<Transcript> transcriptStream) // No CancellationToken argument here, otherwise SignalR binder fails!
    {
        var cancellationToken = Context.GetHttpContext()!.RequestAborted;
        var completeTask = await TranscriptStreamServer.StartWrite(streamId, transcriptStream, cancellationToken).ConfigureAwait(false);
        await _ackStream.Writer
            .WriteAsync(new Ack(AckType.Received, StreamType.Transcription, streamId), CancellationToken.None)
            .ConfigureAwait(false);

        _ = BackgroundTask.Run(async () => {
            await completeTask.ConfigureAwait(false);
            await _ackStream.Writer
                .WriteAsync(new Ack(AckType.Completed, StreamType.Transcription, streamId), CancellationToken.None)
                .ConfigureAwait(false);
        }, CancellationToken.None);
    }
}
