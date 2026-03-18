using ActualChat.Audio;
using ActualChat.Diagnostics;
using ActualChat.Hosting;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

public class StreamServer(IServiceProvider services) : IStreamServer
{
    private readonly bool _preferThisNode = services.HostInfo().HasRole(HostRole.OneServer);

    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private IAudioStreamingBackend Backend { get; } = services.GetRequiredService<IAudioStreamingBackend>();
    private ILogger Log { get; } = services.LogFor<StreamServer>();

    public async Task<RpcStream<byte[]>?> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        // We must return another RpcStream here - they aren't "shareable"
        var source = await Backend.GetAudio(StreamId.Parse(streamId), skipTo, cancellationToken).ConfigureAwait(false);
        return source == null ? null : RpcStream.New(source);
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
            Log.LogError(e, "Error getting transcript for stream #{StreamId}", streamId);
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

    public async Task PushAudio(
        Session session, string chatId, string? repliedChatEntryId,
        double clientStartOffset, int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken)
    {
        var chatIdTyped = ChatId.Parse(chatId);
        var repliedEntryIdTyped = ChatEntryId.ParseNullable(repliedChatEntryId);

        var nodes = MeshWatcher.State.Value.LiveNodesByRole[HostRole.AudioBackend];
        if (nodes.Length == 0) {
            Log.LogError("No nodes serving {Role} role!", HostRole.AudioBackend);
            return; // No backends
        }

        var nodeRef = _preferThisNode ? MeshWatcher.ThisNode.Ref : nodes.GetRandom().Ref;
        var streamId = StreamId.New(nodeRef);
        var audioRecord = new AudioRecord(streamId, session, chatIdTyped, clientStartOffset, repliedEntryIdTyped);
        var newFrameStream = RpcStream.New(frameStream);
        Log.LogInformation("PushAudio: {AudioRecord}", audioRecord);

        using var stopCts = new CancellationTokenSource(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        await Backend.ProcessAudio(audioRecord, preSkip, newFrameStream, stopCts.Token).ConfigureAwait(false);
    }
}
