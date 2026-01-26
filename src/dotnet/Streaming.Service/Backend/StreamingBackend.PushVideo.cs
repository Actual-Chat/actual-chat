using ActualChat.Chat;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public partial class StreamingBackend
{
    public virtual async Task PushVideo(
        VideoRecord record,
        RpcStream<VideoFrame> videoStream,
        CancellationToken cancellationToken)
    {
        ValidateStreamId(record.StreamId);
        Log.LogTrace(nameof(PushVideo) + ": record #{StreamId} = {Record}", record.StreamId, record);

        var delayedCts = cancellationToken.CreateDelayedTokenSource(Constants.Video.CancellationDelay);
        var delayedCancellationToken = delayedCts.Token;

        try {
            await PushVideoInternal(record, videoStream, delayedCancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Error pushing video stream {StreamId}", record.StreamId);
            throw;
        }
        finally {
            delayedCts.CancelAndDisposeSilently();
        }
    }

    private async Task PushVideoInternal(
        VideoRecord record,
        IAsyncEnumerable<VideoFrame> videoFrames,
        CancellationToken cancellationToken)
    {
        var beginsAt = Clocks.SystemClock.Now;
        var rules = await Chats.GetRules(record.Session, record.ChatId, cancellationToken)
            .ConfigureAwait(false);
        rules.Require(ChatPermissions.Write);

        var author = await Authors
            .EnsureJoined(record.Session, record.ChatId, cancellationToken)
            .ConfigureAwait(false);

        // Register stream for real-time signaling
        var streamInfo = new VideoStreamInfo(
            record.StreamId,
            record.ChatId,
            author.Id,
            record.Format,
            beginsAt);
        await Commander.Call(new RealtimeStreamingBackend_RegisterVideoStream(streamInfo), cancellationToken)
            .ConfigureAwait(false);

        try {
            // Publish video stream for real-time viewing
            // No processing - just forward to StreamStore for memoization
            Log.LogInformation("PushVideoInternal: Publishing stream {StreamId} to StreamStore", record.StreamId);

            // Debug: wrap stream to count frames being published
            var frameCount = 0;
            async IAsyncEnumerable<VideoFrame> LogFrames(IAsyncEnumerable<VideoFrame> source)
            {
                await foreach (var frame in source.WithCancellation(cancellationToken)) {
                    frameCount++;
                    if (frameCount <= 3 || frameCount % 30 == 0 || frame.IsKeyFrame)
                        Log.LogInformation("PushVideoInternal memoizing frame #{Count}: Offset={Offset}ms, Size={Size}, IsKey={IsKey}, DescLen={DescLen}",
                            frameCount, frame.Offset.TotalMilliseconds, frame.Data?.Length ?? 0, frame.IsKeyFrame, frame.Description?.Length ?? 0);
                    yield return frame;
                }
                Log.LogInformation("PushVideoInternal stream completed with {Count} frames", frameCount);
            }

            await _videoStreams.Publish(record.StreamId, LogFrames(videoFrames)).ConfigureAwait(false);
        }
        finally {
            // Unregister stream when it ends
            await Commander.Call(
                new RealtimeStreamingBackend_UnregisterVideoStream(record.StreamId, record.ChatId),
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
