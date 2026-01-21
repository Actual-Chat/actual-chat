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
            var stream = videoStream.AsAsyncEnumerable();
            await PushVideoInternal(record, stream, delayedCancellationToken)
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

        var recordedAt = default(Moment) + TimeSpan.FromSeconds(record.ClientStartOffset);

        // Publish video stream for real-time viewing
        // No processing - just forward to StreamStore for memoization
        await _videoStreams.Publish(record.StreamId, videoFrames).ConfigureAwait(false);


        // Create video entry in chat (similar to audio entry)
        // var videoEntryId = VideoEntryId.New(record.ChatId, 0);
        // var command = new ChatsBackend_ChangeEntry(
        //     videoEntryId,
        //     null,
        //     Change.Create(new ChatEntryDiff {
        //         AuthorId = author.Id,
        //         Content = "",
        //         StreamId = record.StreamId.Value,
        //         BeginsAt = beginsAt,
        //         ClientSideBeginsAt = recordedAt,
        //     }));
        //
        // var videoEntry = await Commander.Call(command, true, cancellationToken)
        //     .ConfigureAwait(false);

        // Wait for stream to complete and finalize entry
        // ... similar to audio finalization
    }
}
