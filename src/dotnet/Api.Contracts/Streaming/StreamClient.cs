using System.Buffers;
using ActualChat.Audio;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public class StreamClient(IServiceProvider services) : IStreamClient
{
    private static readonly int StreamBufferSize = 32;

    private IServiceProvider Services { get; } = services;

    private IStreamServer StreamServer => field ??= Services.GetRequiredService<IStreamServer>();
    private ILogger AudioSourceLog => field ??= Services.LogFor<AudioSource>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<AudioSource> GetAudio(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudio({StreamId}, SkipTo = {SkipTo})", streamId, skipTo.ToShortString());
        var rpcStream = await StreamServer.GetAudio(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        var stream = rpcStream?.AsAsyncEnumerable() ?? AsyncEnumerable.Empty<byte[]>();
        var (headerDataTask, dataStream) = stream
            .SuppressException<byte[], RpcReconnectFailedException>(cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken)
            .SplitHead(cancellationToken);
        var frameStream = dataStream
            .Select((data, i) => new AudioFrame {
                Data = data,
                Offset = TimeSpan.FromMilliseconds(i * Constants.Audio.OpusFrameDurationMs), // we support only 20-ms packets
                Duration = Constants.Audio.OpusFrameDuration,
            });

        var headerData = await headerDataTask.ConfigureAwait(false);
        var headerDataSequence = new ReadOnlySequence<byte>(headerData);
        var header = ActualOpusStreamHeader.Parse(ref headerDataSequence);
        return new AudioSource(
            header.CreatedAt,
            header.Format,
            frameStream,
            TimeSpan.Zero,
            AudioSourceLog,
            cancellationToken);
    }

    public async IAsyncEnumerable<TranscriptDiff> GetTranscript(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RpcStream<TranscriptDiff>? diffs;
        try {
            Log.LogDebug("GetTranscript({StreamId})", streamId);
            diffs = await StreamServer.GetTranscript(streamId, cancellationToken).ConfigureAwait(false);
            if (diffs == null)
                yield break;
        }
        catch (RpcReconnectFailedException) {
            yield break;
        }

        var diffStream = diffs.SuppressException<TranscriptDiff, RpcReconnectFailedException>(cancellationToken);
        await foreach (var diff in diffStream.ConfigureAwait(false))
            yield return diff;
    }

    public Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken)
        => StreamServer.ReportAudioLatency(latency, cancellationToken);
}
