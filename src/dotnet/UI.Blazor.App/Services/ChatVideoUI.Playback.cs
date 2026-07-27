using ActualChat.Video;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    [ComputeMethod]
    public virtual async Task<ApiArray<VideoStreamInfo>> GetActiveVideoStreams(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false))
            return [];

        return await LiveVideoStreams
            .List(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false))
            return 0;

        return await LiveVideoStreams
            .GetMemberCount(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<VideoStreamInfo[]> GetRemoteStreams(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var videoStreams = await GetActiveVideoStreams(chatId, cancellationToken).ConfigureAwait(false);
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        return videoStreams
            .Where(s => s.AuthorId != ownAuthor?.Id && !IsLocallyEndedRemoteStream(s.StreamId))
            .ToArray();
    }

    [ComputeMethod]
    public virtual async Task<bool> IsAnyoneVideoStreaming(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var streams = await GetActiveVideoStreams(chatId, cancellationToken).ConfigureAwait(false);
        return streams.Count > 0;
    }

    [ComputeMethod]
    public virtual async Task<bool> HasRemoteStreams(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var videoStreams = await GetActiveVideoStreams(chatId, cancellationToken).ConfigureAwait(false);
        if (videoStreams.Count == 0)
            return false;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        return videoStreams.Any(s => s.AuthorId != ownAuthor?.Id && !IsLocallyEndedRemoteStream(s.StreamId));
    }

    public void NotifyRemoteStreamEnded(ChatId chatId, StreamId streamId, bool isEndedSuccessfully, bool isTerminal)
    {
        if (isEndedSuccessfully)
            Interlocked.Exchange(ref _remoteStreamEndedSuccessfully, 1);
        if (!isTerminal)
            return;

        _locallyEndedRemoteStreams[streamId.Value] = CpuTimestamp.Now;
        InvalidateRemoteStreamAccessors(chatId);
    }

    public bool ConsumeRemoteStreamEndedSuccessfully()
        => Interlocked.Exchange(ref _remoteStreamEndedSuccessfully, 0) != 0;

    // Private methods

    private bool IsLocallyEndedRemoteStream(StreamId streamId)
    {
        if (!_locallyEndedRemoteStreams.TryGetValue(streamId.Value, out var endedAt))
            return false;
        if (endedAt.Elapsed <= LocallyEndedRemoteStreamRetention)
            return true;

        _locallyEndedRemoteStreams.TryRemove(streamId.Value, out _);
        return false;
    }

    private void InvalidateRemoteStreamAccessors(ChatId chatId)
    {
        using (Invalidation.Begin()) {
            _ = GetRemoteStreams(chatId, default);
            _ = HasRemoteStreams(chatId, default);
        }
    }
}
