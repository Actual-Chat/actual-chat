using System.Buffers;
using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public class StreamClient(IServiceProvider services) : IStreamClient
{
    private static readonly int StreamBufferSize = 64;

    private ILogger? _log;
    private ILogger? _audioSourceLog;
    private IStreamServer? _streamServer;

    private IServiceProvider Services { get; } = services;
    private IStreamServer StreamServer => _streamServer ??= Services.GetRequiredService<IStreamServer>();
    private ILogger AudioSourceLog => _audioSourceLog ??= Services.LogFor<AudioSource>();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public async Task<AudioSource> GetAudio(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudio({StreamId}, SkipTo = {SkipTo})", streamId.Value, skipTo.ToShortString());
        var rpcStream = await StreamServer.GetAudio(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        var stream = rpcStream?.AsAsyncEnumerable() ?? AsyncEnumerable.Empty<byte[]>();
        var (headerDataTask, dataStream) = stream
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
        Symbol streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Log.LogDebug("GetTranscript({StreamId})", streamId.Value);
        var diffs = await StreamServer.GetTranscript(streamId, cancellationToken).ConfigureAwait(false);
        if (diffs == null)
            yield break;

        // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
        await foreach (var diff in diffs.ConfigureAwait(false))
            yield return diff;
    }

    public Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken)
        => StreamServer.ReportAudioLatency(latency, cancellationToken);
}
