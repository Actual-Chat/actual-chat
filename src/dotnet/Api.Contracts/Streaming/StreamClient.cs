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
    private ILogger AudioSourceLog => field ??= Services.LogFor<AudioSource>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<AudioSource> GetAudio(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudio(#{StreamId}, SkipTo = {SkipTo})", streamId, skipTo.ToShortString());
        var rpcStream = await StreamServer.GetAudio(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        var stream = (IAsyncEnumerable<AudioFrame>?)rpcStream ?? AsyncEnumerable.Empty<AudioFrame>();
        var (headerFrameTask, dataStream) = stream
            .SuppressException<AudioFrame, RpcReconnectFailedException>(cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken)
            .SplitHead(cancellationToken);

        var headerFrame = await headerFrameTask.ConfigureAwait(false);
        var headerDataSequence = new ReadOnlySequence<byte>(headerFrame.Data);
        var header = ActualOpusStreamHeader.Parse(ref headerDataSequence);
        return new AudioSource(
            header.CreatedAt,
            header.Format,
            dataStream,
            skipTo,
            AudioSourceLog,
            cancellationToken);
    }

    public async IAsyncEnumerable<TranscriptDiff> GetTranscript(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RpcStream<TranscriptDiff>? diffs;
        try {
            Log.LogDebug("GetTranscript(#{StreamId})", streamId);
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

    public Task PushAudio(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartOffset,
        int preSkip,
        IAsyncEnumerable<AudioFrame> frameStream,
        CancellationToken cancellationToken)
    {
        var rpcStream = RpcStream.New(frameStream);
        return StreamServer.PushAudio(
            session,
            chatId,
            repliedChatEntryId,
            clientStartOffset,
            preSkip,
            rpcStream,
            cancellationToken);
    }

    public Task PushVideo(
        Session session,
        string chatId,
        double clientStartOffset,
        VideoFormat format,
        IAsyncEnumerable<VideoFrame> frameStream,
        StreamKind streamKind,
        CancellationToken cancellationToken)
    {
        var rpcStream = RpcStream.New(frameStream);
        return StreamServer.PushVideo(
            session,
            chatId,
            clientStartOffset,
            format,
            rpcStream,
            streamKind,
            cancellationToken);
    }
}
