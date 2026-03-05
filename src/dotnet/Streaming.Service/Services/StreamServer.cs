using ActualChat.Audio;
using ActualChat.Diagnostics;
using ActualChat.Hosting;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

public class StreamServer(IServiceProvider services) : IStreamServer
{
    private IStreamingBackend Backend { get; } = services.GetRequiredService<IStreamingBackend>();
    private ILogger Log { get; } = services.LogFor<StreamServer>();

    public async Task<RpcStream<byte[]>?> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        // We must return another RpcStream here - they aren't "shareable"
        var source = await Backend.GetAudio(StreamId.Parse(streamId), skipTo, cancellationToken).ConfigureAwait(false);
        return source == null ? null : RpcStream.New((IAsyncEnumerable<byte[]>)source);
    }

    public async Task<RpcStream<TranscriptDiff>?> GetTranscript(string streamId, CancellationToken cancellationToken)
    {
        RpcStream<TranscriptDiff>? diffs = null;
        try {
            diffs = await Backend.GetTranscript(StreamId.Parse(streamId), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (RpcReconnectFailedException) { }
        catch (Exception e) {
            Log.LogError(e, "Error getting transcript for {StreamId}", streamId);
        }

        if (diffs == null)
            return null;

        var diffStream = diffs
            .SuppressException<TranscriptDiff, RpcReconnectFailedException>(cancellationToken)
            .SuppressCancellation(cancellationToken);
        return RpcStream.New(diffStream);
    }

    public Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken)
    {
        AppMeters.AudioLatency.Record(latency.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public Task PushAudio(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartOffset,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken)
    {
        var chatIdTyped = ChatId.Parse(chatId);
        var repliedEntryIdTyped = TextEntryId.ParseNullable(repliedChatEntryId);
        var nodes = services.MeshWatcher().State.Value.LiveNodesByRole[HostRole.AudioBackend];
        if (nodes.Length == 0) {
            Log.LogError("PushAudio: No nodes serving {Role} role!", HostRole.AudioBackend);
            return Task.CompletedTask;
        }
        var nodeRef = nodes.GetRandom().Ref;
        var streamId = StreamId.New(nodeRef);
        var record = new AudioRecord(streamId, session, chatIdTyped, clientStartOffset, repliedEntryIdTyped);
        Log.LogInformation("PushAudio: {AudioRecord}", record);
        return Backend.ProcessAudio(record, preSkip, frameStream, cancellationToken);
    }
}
