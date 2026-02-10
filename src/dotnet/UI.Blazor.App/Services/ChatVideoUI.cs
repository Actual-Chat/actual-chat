using ActualChat.Streaming;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Provides reactive access to video stream data for the current chat.
/// Player lifecycle management is handled by VideoTrackPlayer components.
/// State is in-memory only (not persisted) since video recording can't survive page refresh.
/// </summary>
public partial class ChatVideoUI : UIServiceBase<AppUIHub>, IComputeService
{
    // Centralized video state
    private readonly IMutableState<ChatId?> _recordingChatId;
    private readonly IMutableState<string?> _selectedCameraDeviceId;
    private readonly IMutableState<bool> _isBackgroundBlurEnabled;
    private readonly IMutableState<string?> _errorMessage;

    private ILiveVideoStreams LiveVideoStreams => Hub.Services.GetRequiredService<ILiveVideoStreams>();
    private IAuthors Authors => Hub.Authors;

    public ChatVideoUI(AppUIHub hub) : base(hub)
    {
        _recordingChatId = StateFactory.NewMutable((ChatId?)null);
        _selectedCameraDeviceId = StateFactory.NewMutable((string?)null);
        _isBackgroundBlurEnabled = StateFactory.NewMutable(false);
        _errorMessage = StateFactory.NewMutable((string?)null);
    }

    // Core state accessors

    [ComputeMethod]
    public virtual async Task<ChatVideoState> GetState(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var recordingChatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        var isRecording = recordingChatId == chatId;

        var selectedCameraDeviceId = isRecording
            ? await _selectedCameraDeviceId.Use(cancellationToken).ConfigureAwait(false)
            : null;
        var isBackgroundBlurEnabled = isRecording
            && await _isBackgroundBlurEnabled.Use(cancellationToken).ConfigureAwait(false);
        var errorMessage = isRecording
            ? await _errorMessage.Use(cancellationToken).ConfigureAwait(false)
            : null;

        return new ChatVideoState(
            chatId,
            isRecording,
            selectedCameraDeviceId,
            isBackgroundBlurEnabled,
            isRecording && errorMessage != null,
            errorMessage);
    }

    [ComputeMethod]
    public virtual async Task<ChatId?> GetRecordingChatId(CancellationToken cancellationToken = default)
        => await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);

    // State mutators

    public void SetRecordingChatId(ChatId? chatId, string? cameraDeviceId = null, bool isBackgroundBlurEnabled = false)
    {
        if (chatId is null) {
            _recordingChatId.Value = null;
            return;
        }

        _recordingChatId.Value = chatId;
        _selectedCameraDeviceId.Value = cameraDeviceId;
        _isBackgroundBlurEnabled.Value = isBackgroundBlurEnabled;
        _errorMessage.Value = null;
    }

    public void SetSelectedCamera(string? cameraDeviceId)
        => _selectedCameraDeviceId.Value = cameraDeviceId;

    public void SetBackgroundBlur(bool enabled)
        => _isBackgroundBlurEnabled.Value = enabled;

    public void SetError(string? errorMessage)
        => _errorMessage.Value = errorMessage;

    // JS callback handlers (called from VideoPanel)

    public void OnRecordingStarted(ChatId chatId)
    {
        if (_recordingChatId.Value == chatId)
            return;
        _recordingChatId.Value = chatId;
        _errorMessage.Value = null;
    }

    public void OnRecordingStopped()
        => _recordingChatId.Value = null;

    public void OnRecordingError(string error)
    {
        _errorMessage.Value = error;
        _recordingChatId.Value = null;
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<VideoStreamInfo>> GetActiveVideoStreams(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return default;

        return await LiveVideoStreams
            .ListActiveStreams(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<AuthorId[]> GetVideoStreamingAuthorIds(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return [];

        return await LiveVideoStreams
            .GetVideoStreamingAuthorIds(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsAnyoneVideoStreaming(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return false;

        var authorIds = await LiveVideoStreams
            .GetVideoStreamingAuthorIds(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
        return authorIds.Length > 0;
    }

    [ComputeMethod]
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return 0;

        return await LiveVideoStreams
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
