using System.Buffers;
using ActualChat.Audio;
using ActualChat.Diagnostics;
using ActualChat.Transcription;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

public sealed class StreamBackendClient : IStreamClient
{
    private IAudioStreamingBackend Backend { get; }
    private IVideoStreamingBackend VideoBackend { get; }
    private MeshWatcher MeshWatcher { get; }
    private ILogger Log { get; }
    private ILogger AudioSourceLog { get; }

    public StreamBackendClient(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        AudioSourceLog = services.LogFor<AudioSource>();
        Backend = services.GetRequiredService<IAudioStreamingBackend>();
        VideoBackend = services.GetRequiredService<IVideoStreamingBackend>();
        MeshWatcher = services.MeshWatcher();
    }

    public Task PushAudio(
        Session session, string chatId, string? repliedChatEntryId,
        double clientStartOffset, int preSkip,
        IAsyncEnumerable<AudioFrame> frameStream,
        CancellationToken cancellationToken)
    {
        // StreamBackendClient runs on the same server, so route directly to backend
        var chatIdTyped = ChatId.Parse(chatId);
        var repliedEntryIdTyped = ChatEntryId.ParseNullable(repliedChatEntryId);
        var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
        var record = new AudioRecord(streamId, session, chatIdTyped, clientStartOffset, repliedEntryIdTyped);
        var rpcStream = RpcStream.New(frameStream);
        return Backend.ProcessAudio(record, preSkip, rpcStream, cancellationToken);
    }

    public async Task<AudioSource> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudio(#{StreamId}, SkipTo = {SkipTo})", streamId, skipTo.ToShortString());
        var rpcStream = await Backend.GetAudio(StreamId.Parse(streamId), skipTo, cancellationToken).ConfigureAwait(false);
        var stream = (IAsyncEnumerable<AudioFrame>?)rpcStream ?? AsyncEnumerable.Empty<AudioFrame>();
        var (headerFrameTask, dataStream) = stream
            .SuppressException<AudioFrame, RpcReconnectFailedException>(cancellationToken)
            .SplitHead(cancellationToken);

        var headerFrame = await headerFrameTask.ConfigureAwait(false);
        var headerDataSequence = new ReadOnlySequence<byte>(headerFrame.Data);
        var header = ActualOpusStreamHeader.Parse(ref headerDataSequence);
        return new AudioSource(
            header.CreatedAt,
            header.Format,
            dataStream,
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
            Log.LogDebug("GetTranscript(#{StreamId})", streamId);
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
            Log.LogError(e, "Error getting transcript for #{StreamId}", streamId);
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
        AppMeters.AudioLatency.Record(latency.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public Task PushVideo(
        Session session, string chatId,
        double clientStartOffset,
        VideoFormat format,
        IAsyncEnumerable<VideoFrame> frameStream,
        StreamKind streamKind,
        CancellationToken cancellationToken)
    {
        var chatIdTyped = ChatId.Parse(chatId);
        var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
        var record = new VideoRecord(streamId, session, chatIdTyped, clientStartOffset, format, streamKind);
        var rpcStream = RpcStream.New(frameStream);
        return VideoBackend.PushVideo(record, rpcStream, cancellationToken);
    }
}
