using System.Buffers;
using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming.Services;

public sealed class StreamBackendClient : IStreamClient
{
    private IStreamingBackend Backend { get; }
    private ILogger Log { get; }
    private ILogger AudioSourceLog { get; }
    private OtelMetrics Metrics { get; }

    public StreamBackendClient(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        AudioSourceLog = services.LogFor<AudioSource>();
        Metrics = services.Metrics();
        Backend = services.GetRequiredService<IStreamingBackend>();
    }

    public async Task<AudioSource> GetAudio(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudio({StreamId}, SkipTo = {SkipTo})", streamId.Value, skipTo.ToShortString());
        var rpcStream = await Backend.GetAudio(new StreamId(streamId), skipTo, cancellationToken).ConfigureAwait(false);
        var stream = rpcStream?.AsAsyncEnumerable() ?? AsyncEnumerable.Empty<byte[]>();
        var (headerDataTask, dataStream) = stream.SplitHead(cancellationToken);
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
        var diffs = await Backend.GetTranscript(new StreamId(streamId), cancellationToken).ConfigureAwait(false);
        if (diffs == null)
            yield break;

        // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
        await foreach(var diff in diffs.ConfigureAwait(false))
            yield return diff;
    }

    public Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken)
    {
        Metrics.AudioLatency.Record((float)latency.TotalMilliseconds);
        return Task.CompletedTask;
    }
}
