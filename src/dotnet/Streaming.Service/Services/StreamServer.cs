using ActualChat.Diagnostics;
using ActualChat.Transcription;
using ActualChat.Video;
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

    public async Task<RpcStream<VideoFrame>?> GetVideo(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        RpcStream<VideoFrame>? frames = null;
        try {
            frames = await Backend.GetVideo(StreamId.Parse(streamId), skipTo, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (RpcReconnectFailedException) { }
        catch (Exception e) {
            Log.LogError(e, "Error getting video for {StreamId}", streamId);
        }

        if (frames == null)
            return null;

        var frameStream = frames
            .SuppressException<VideoFrame, RpcReconnectFailedException>(cancellationToken)
            .SuppressCancellation(cancellationToken);
        return RpcStream.New(frameStream);
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
        AppMeters.AudioLatency.Record((float)latency.TotalMilliseconds);
        return Task.CompletedTask;
    }
}
