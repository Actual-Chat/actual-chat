using ActualChat.Streaming;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Provides reactive access to video stream data for the current chat.
/// Player lifecycle management is handled by VideoTrackPlayer components.
/// </summary>
public partial class ChatVideoUI : UIServiceBase<AppUIHub>, IComputeService
{
    private readonly IMutableState<bool> _stopVideoRecordingRequested;
    private readonly IMutableState<bool> _isLocalVideoRecording;

    private IRealtimeStreaming RealtimeStreaming => Hub.Services.GetRequiredService<IRealtimeStreaming>();
    private IAuthors Authors => Hub.Authors;

    public IState<bool> StopVideoRecordingRequested => _stopVideoRecordingRequested;
    public IState<bool> IsLocalVideoRecording => _isLocalVideoRecording;

    public ChatVideoUI(AppUIHub hub) : base(hub)
    {
        _stopVideoRecordingRequested = StateFactory.NewMutable(false);
        _isLocalVideoRecording = StateFactory.NewMutable(false);
    }

    public void RequestStopVideoRecording()
        => _stopVideoRecordingRequested.Value = true;

    public void ResetStopRecordingRequest()
        => _stopVideoRecordingRequested.Value = false;

    public void SetLocalVideoRecording(bool isRecording)
        => _isLocalVideoRecording.Value = isRecording;

    [ComputeMethod]
    public virtual async Task<ActiveVideoStreams?> GetActiveVideoStreams(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return null;

        return await RealtimeStreaming
            .GetActiveVideoStreams(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<AuthorId[]> GetVideoStreamingAuthorIds(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return [];

        return await RealtimeStreaming
            .GetVideoStreamingAuthorIds(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsAnyoneVideoStreaming(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return false;

        var authorIds = await RealtimeStreaming
            .GetVideoStreamingAuthorIds(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
        return authorIds.Length > 0;
    }

    [ComputeMethod]
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return 0;

        return await RealtimeStreaming
            .GetVideoStreamMemberCount(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsOwnVideoStreaming(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return false;

        var authorIds = await GetVideoStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        if (authorIds.Length == 0)
            return false;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        return ownAuthor != null && authorIds.Contains(ownAuthor.Id);
    }
}
