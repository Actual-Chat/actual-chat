using System.Buffers;
using ActualChat.Audio;
using ActualChat.Transcription;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public class StreamClient(IServiceProvider services) : IStreamClient
{
    private static readonly int StreamBufferSize = 32;

    private IServiceProvider Services { get; } = services;

    private IStreamServer StreamServer => field ??= Services.GetRequiredService<IStreamServer>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger AudioSourceLog => field ??= Services.LogFor<AudioSource>();
    private ILogger VideoSourceLog => field ??= Services.LogFor<VideoSource>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<AudioSource> GetAudio(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudio({StreamId}, SkipTo = {SkipTo})", streamId, skipTo.ToShortString());
        var rpcStream = await StreamServer.GetAudio(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        var stream = (IAsyncEnumerable<byte[]>?)rpcStream ?? AsyncEnumerable.Empty<byte[]>();
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

    public async Task<VideoSource> GetVideo(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("GetVideo({StreamId}, SkipTo = {SkipTo})", streamId, skipTo.ToShortString());
        var rpcStream = await StreamServer.GetVideo(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        var stream = rpcStream ?? AsyncEnumerable.Empty<VideoFrame>();
        var frameStream = stream
            .SuppressException<VideoFrame, RpcReconnectFailedException>(cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);

        // Extract format from the first keyframe
        var (firstFrameTask, restStream) = frameStream.SplitHead(cancellationToken);
        var firstFrame = await firstFrameTask.ConfigureAwait(false);

        var format = new VideoFormat {
            Codec = firstFrame.Codec ?? "avc1",
            Width = firstFrame.Width,
            Height = firstFrame.Height,
            CodecSettings = firstFrame.Description != null
                ? Convert.ToBase64String(firstFrame.Description)
                : "",
        };

        // Prepend first frame back to stream
        var fullStream = restStream.Prepend(firstFrame);

        return new VideoSource(
            Clocks.SystemClock.Now,
            format,
            fullStream,
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

    public Task ReportVideoLatency(string streamId, TimeSpan latency, CancellationToken cancellationToken)
        => StreamServer.ReportVideoLatency(streamId, latency, cancellationToken);

    public async IAsyncEnumerable<VideoQualityPreset> ObserveStreamQualityRequests(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rpcStream = await StreamServer.ObserveStreamQualityRequests(streamId, cancellationToken).ConfigureAwait(false);
        if (rpcStream == null)
            yield break;

        var stream = rpcStream
            .SuppressException<VideoQualityPreset, RpcReconnectFailedException>(cancellationToken);
        await foreach (var preset in stream.ConfigureAwait(false))
            yield return preset;
    }
}
