using System.Buffers;
using ActualChat.Audio;
using ActualChat.Diagnostics;
using ActualChat.Transcription;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

public sealed class StreamBackendClient : IStreamClient
{
    private IServiceProvider Services { get; }
    private IStreamingBackend Backend { get; }
    private ILogger Log { get; }
    private ILogger AudioSourceLog { get; }
    private ILogger VideoSourceLog { get; }
    private MomentClockSet Clocks => field ??= Services.Clocks();

    public StreamBackendClient(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        AudioSourceLog = services.LogFor<AudioSource>();
        VideoSourceLog = services.LogFor<VideoSource>();
        Backend = services.GetRequiredService<IStreamingBackend>();
    }

    public async Task<AudioSource> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudio({StreamId}, SkipTo = {SkipTo})", streamId, skipTo.ToShortString());
        var rpcStream = await Backend.GetAudio(StreamId.Parse(streamId), skipTo, cancellationToken).ConfigureAwait(false);
        var stream = (IAsyncEnumerable<byte[]>?)rpcStream ?? AsyncEnumerable.Empty<byte[]>();
        var (headerDataTask, dataStream) = stream
            .SuppressException<byte[], RpcReconnectFailedException>(cancellationToken)
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

    public async Task<VideoSource> GetVideo(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        Log.LogDebug("GetVideo({StreamId}, SkipTo = {SkipTo})", streamId, skipTo.ToShortString());
        var rpcStream = await Backend.GetVideo(StreamId.Parse(streamId), skipTo, cancellationToken).ConfigureAwait(false);
        var stream = rpcStream?.AsAsyncEnumerable() ?? AsyncEnumerable.Empty<VideoFrame>();
        var frameStream = stream
            .SuppressException<VideoFrame, RpcReconnectFailedException>(cancellationToken)
            .SuppressCancellation(cancellationToken);

        // For video, we use a simpler approach - the first frame contains format info
        // The VideoSource will handle skipTo internally
        var format = VideoSource.DefaultFormat;
        return new VideoSource(
            Clocks.SystemClock.Now,
            format,
            frameStream,
            skipTo,
            VideoSourceLog,
            cancellationToken);
    }

    public async IAsyncEnumerable<TranscriptDiff> GetTranscript(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RpcStream<TranscriptDiff>? diffs;
        try {
            Log.LogDebug("GetTranscript({StreamId})", streamId);
            diffs = await Backend.GetTranscript(StreamId.Parse(streamId), cancellationToken).ConfigureAwait(false);
            if (diffs == null)
                yield break;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            yield break;
        }
        catch (RpcReconnectFailedException) {
            yield break;
        }
        catch (Exception e) {
            Log.LogError(e, "Error getting transcript for {StreamId}", streamId);
            yield break;
        }

        // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
        var diffStream = diffs
            .SuppressException<TranscriptDiff, RpcReconnectFailedException>(cancellationToken)
            .SuppressCancellation(cancellationToken);
        await foreach(var diff in diffStream.ConfigureAwait(false))
            yield return diff;
    }

    public Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken)
    {
        AppMeters.AudioLatency.Record((float)latency.TotalMilliseconds);
        return Task.CompletedTask;
    }
}
