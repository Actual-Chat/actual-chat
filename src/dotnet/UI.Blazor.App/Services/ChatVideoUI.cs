using ActualChat.Streaming;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Provides reactive access to video stream data for the current chat.
/// Player lifecycle management is handled by VideoTrackPlayer components.
/// State is in-memory only (not persisted) since video recording can't survive page refresh.
/// </summary>
public partial class ChatVideoUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    // Centralized video state
    private readonly MutableState<ChatId?> _recordingChatId;
    private readonly MutableState<string?> _selectedCameraDeviceId;
    private readonly MutableState<bool> _isBackgroundBlurEnabled;
    private readonly MutableState<string?> _errorMessage;

    // Tracks which chat the user is currently watching video in (in-memory, resets on reload)
    private readonly MutableState<ChatId?> _watchingChatId;

    // Active speaker focus state
    private readonly MutableState<AuthorId?> _focusedSpeakerId;
    private readonly MutableState<AuthorId?> _previousFocusedSpeakerId;
    private CancellationTokenSource? _focusDebounceCts;
    private AuthorId? _pendingFocusCandidate;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ILiveVideoStreams LiveVideoStreams => Hub.Services.GetRequiredService<ILiveVideoStreams>();
    private IAuthors Authors => Hub.Authors;

    public ChatVideoUI(AppUIHub hub) : base(hub)
    {
        _recordingChatId = StateFactory.NewMutable((ChatId?)null);
        _selectedCameraDeviceId = StateFactory.NewMutable((string?)null);
        _isBackgroundBlurEnabled = StateFactory.NewMutable(false);
        _errorMessage = StateFactory.NewMutable((string?)null);
        _watchingChatId = StateFactory.NewMutable((ChatId?)null);
        _focusedSpeakerId = StateFactory.NewMutable((AuthorId?)null);
        _previousFocusedSpeakerId = StateFactory.NewMutable((AuthorId?)null);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    // Core state accessors

    [ComputeMethod]
    public virtual async Task<ChatVideoState> GetState(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var recordingChatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        var isRecording = recordingChatId == chatId;

        var watchingChatId = await _watchingChatId.Use(cancellationToken).ConfigureAwait(false);
        var isWatching = watchingChatId == chatId;

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
            isWatching,
            selectedCameraDeviceId,
            isBackgroundBlurEnabled,
            isRecording && errorMessage != null,
            errorMessage);
    }

    [ComputeMethod]
    public virtual async Task<ChatId?> GetRecordingChatId(CancellationToken cancellationToken = default)
        => await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<ChatId?> GetWatchingChatId(CancellationToken cancellationToken = default)
        => await _watchingChatId.Use(cancellationToken).ConfigureAwait(false);

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
        SetWatching(chatId);
    }

    public void SetSelectedCamera(string? cameraDeviceId)
        => _selectedCameraDeviceId.Value = cameraDeviceId;

    public void SetBackgroundBlur(bool enabled)
        => _isBackgroundBlurEnabled.Value = enabled;

    public void SetError(string? errorMessage)
        => _errorMessage.Value = errorMessage;

    public void SetWatching(ChatId? chatId)
    {
        if (_watchingChatId.Value == chatId)
            return;
        _watchingChatId.Value = chatId;
        // Ensure listening is on when starting to watch
        if (chatId is not null)
            _ = ChatAudioUI.SetListeningState(chatId, true);
    }

    public bool HasJoinedVideoCall(ChatId chatId)
        => _watchingChatId.Value == chatId;

    public void ResumeRecording(ChatId chatId)
    {
        // Resume recording without overwriting camera/blur settings preserved from the previous recording
        _recordingChatId.Value = chatId;
        _errorMessage.Value = null;
        SetWatching(chatId);
    }

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
        // Don't clear _watchingChatId — user stays watching remote streams, can retry or hang up
    }

    // Active speaker focus

    [ComputeMethod]
    public virtual async Task<AuthorId?> GetFocusedSpeakerId(CancellationToken cancellationToken = default)
        => await _focusedSpeakerId.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<AuthorId?> GetPreviousFocusedSpeakerId(CancellationToken cancellationToken = default)
        => await _previousFocusedSpeakerId.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<ApiArray<VideoStreamInfo>> GetActiveVideoStreams(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return default;

        return await LiveVideoStreams
            .List(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> GetVideoStreamingAuthorIds(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return default;

        return await LiveVideoStreams
            .GetAuthorIds(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsAnyoneVideoStreaming(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return false;

        var authorIds = await LiveVideoStreams
            .GetAuthorIds(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
        return authorIds.Count > 0;
    }

    [ComputeMethod]
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return 0;

        return await LiveVideoStreams
            .GetMemberCount(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsOwnVideoStreaming(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return false;

        var authorIds = await GetVideoStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        if (authorIds.Count == 0)
            return false;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        return ownAuthor != null && authorIds.Contains(ownAuthor.Id);
    }
}
