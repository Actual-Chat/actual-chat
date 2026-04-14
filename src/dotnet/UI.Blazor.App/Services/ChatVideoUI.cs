using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;
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
    private readonly MutableState<ChatId?> _lastRecordingChatId;
    private readonly MutableState<string?> _selectedCameraDeviceId;
    private readonly MutableState<bool> _isBackgroundBlurEnabled;
    private readonly MutableState<string?> _errorMessage;
    private readonly MutableState<bool> _isScreencasting;

    // Tracks which chat the user is currently watching video in (in-memory, resets on reload)
    private readonly MutableState<ChatId?> _watchingChatId;

    // UI-only: hides video panel without affecting watching/recording state
    private readonly MutableState<bool> _isVideoPanelCollapsed;

    // Active speaker focus state
    private readonly MutableState<AuthorId?> _focusedSpeakerId;
    private readonly MutableState<AuthorId?> _previousFocusedSpeakerId;
    private CancellationTokenSource? _focusDebounceCts;
    private AuthorId? _pendingFocusCandidate;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    public ILiveVideoStreams LiveVideoStreams => field ??= Services.GetRequiredService<ILiveVideoStreams>();

    public ChatVideoUI(AppUIHub hub) : base(hub)
    {
        _recordingChatId = StateFactory.NewMutable((ChatId?)null);
        _lastRecordingChatId = StateFactory.NewMutable((ChatId?)null);
        _selectedCameraDeviceId = StateFactory.NewMutable((string?)null);
        _isBackgroundBlurEnabled = StateFactory.NewMutable(false);
        _errorMessage = StateFactory.NewMutable((string?)null);
        _isScreencasting = StateFactory.NewMutable(false);
        _watchingChatId = StateFactory.NewMutable((ChatId?)null);
        _isVideoPanelCollapsed = StateFactory.NewMutable(false);
        _focusedSpeakerId = StateFactory.NewMutable((AuthorId?)null);
        _previousFocusedSpeakerId = StateFactory.NewMutable((AuthorId?)null);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    // Core state accessors

    [ComputeMethod]
    public virtual async Task<StreamKind?> GetOwnStreamKind(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var isVideoEnabled = await IsVideoStreamingEnabled(cancellationToken).ConfigureAwait(false);
        if (!isVideoEnabled)
            return null;

        var recordingChatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (recordingChatId != chatId)
            return null;

        var isScreencasting = await _isScreencasting.Use(cancellationToken).ConfigureAwait(false);
        return isScreencasting ? StreamKind.Screencast : StreamKind.Webcam;
    }

    [ComputeMethod]
    public virtual async Task<string?> GetLastVideoRecorderError(CancellationToken cancellationToken = default)
        => await _errorMessage.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<ChatId?> GetWatchingChatId(CancellationToken cancellationToken = default)
        => await _watchingChatId.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> GetIsVideoPanelCollapsed(CancellationToken cancellationToken = default)
        => await _isVideoPanelCollapsed.Use(cancellationToken).ConfigureAwait(false);

    // State mutators

    public void StopStreaming()
    {
        _recordingChatId.Value = null;
        _isScreencasting.Value = false;
    }

    public void StartScreenCasting(ChatId chatId)
    {
        _recordingChatId.Value = chatId;
        _isScreencasting.Value = true;
        OpenVideoPanel(chatId);
    }

    public void SetSelectedCamera(string? cameraDeviceId)
        => _selectedCameraDeviceId.Value = cameraDeviceId;

    public void SetBackgroundBlur(bool enabled)
        => _isBackgroundBlurEnabled.Value = enabled;

    public void CloseVideoPanel()
        => SetWatching(null);

    public void OpenVideoPanel(ChatId chatId)
        => SetWatching(chatId);

    public bool HasJoinedVideoCall(ChatId chatId)
        => _watchingChatId.Value == chatId && _lastRecordingChatId.Value == chatId;

    public void SetVideoPanelCollapsed(bool collapsed)
        => _isVideoPanelCollapsed.Value = collapsed;

    public void ResumeVideoStreaming(ChatId chatId)
    {
        // Resume recording without overwriting camera/blur settings preserved from the previous recording
        _recordingChatId.Value = chatId;
        OpenVideoPanel(chatId);
    }

    // JS callback handlers (called from VideoPanel)

    public void OnRecordingStarted(ChatId chatId)
    {
    }

    public void OnRecordingStopped()
        => StopStreaming();

    public void OnRecordingError(string error)
    {
        _errorMessage.Value = error;
        StopStreaming();
        // Don't close the video panel. User stays watching remote streams, can retry or hang up
    }

    // Device enumeration

    public async Task<VideoDevice[]> EnumerateVideoDevices()
    {
        try {
            var jsMethod = $"{BlazorUIAppModule.ImportName}.VideoRecorder.enumerateDevices";
            return await JS.InvokeAsync<VideoDevice[]>(jsMethod).ConfigureAwait(false);
        }
        catch(Exception e) {
            Log.LogError(e, "EnumerateVideoDevices failed");
            return [];
        }
    }

    public async Task SwitchCamera()
    {
        var devices = await EnumerateVideoDevices().ConfigureAwait(false);
        if (devices.Length <= 1)
            return;

        var localAppSettings = LocalSettings.LocalAppSettings();
        var settings = await localAppSettings.Get().ConfigureAwait(false);
        var currentId = settings.SelectedCameraDeviceId ?? "";
        var currentIndex = Array.FindIndex(devices, d => d.DeviceId == currentId);
        var nextIndex = (currentIndex + 1) % devices.Length;
        var nextDevice = devices[nextIndex];
        await localAppSettings.Update(
            s => s with { SelectedCameraDeviceId = nextDevice.DeviceId }).ConfigureAwait(false);
        SetSelectedCamera(nextDevice.DeviceId);
    }

    // Modal helpers

    public void JoinVideoSession(ChatId chatId)
    {
        _ = JoinInternal();
        return;

        async Task JoinInternal(CancellationToken cancellationToken = default)
        {
            var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat is null)
                return;

            var model = new JoinVideoCallModal.Model(chat, JoinVideoCallModal.VideoCallMode.Join);
            var modeRef = await ModalUI.Show(model, CancellationToken.None).ConfigureAwait(true);
            await modeRef.WhenClosed.ConfigureAwait(true);
            if (!model.IsConfirmed)
                return;

            StartVideoStreaming(chatId, model.SelectedDeviceId, model.IsBlurEnabled);
        }
    }

    public void ChangeVideoSessionSettings(ChatId chatId)
    {
        _ = ChangeInternal();
        return;

        async Task ChangeInternal()
        {
            var chat = await Chats.Get(Session, chatId, default).ConfigureAwait(false);
            if (chat is null)
                return;

            var model = new JoinVideoCallModal.Model(chat, JoinVideoCallModal.VideoCallMode.Settings);
            var modeRef = await ModalUI.Show(model, CancellationToken.None).ConfigureAwait(true);
            await modeRef.WhenClosed.ConfigureAwait(true);
            if (!model.IsConfirmed)
                return;

            SetBackgroundBlur(model.IsBlurEnabled);
        }
    }

    // Active speaker focus

    [ComputeMethod]
    public virtual async Task<AuthorId?> GetFocusedSpeakerId(CancellationToken cancellationToken = default)
        => await _focusedSpeakerId.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<AuthorId?> GetPreviousFocusedSpeakerId(CancellationToken cancellationToken = default)
        => await _previousFocusedSpeakerId.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<ApiArray<VideoStreamInfo>> GetActiveVideoStreams(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var isVideoEnabled = await IsVideoStreamingEnabled(cancellationToken).ConfigureAwait(false);
        if (!isVideoEnabled)
            return [];

        return await LiveVideoStreams
            .List(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var isEnabled = await IsVideoStreamingEnabled(cancellationToken).ConfigureAwait(false);
        if (!isEnabled)
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
        return videoStreams.Where(s => s.AuthorId != ownAuthor?.Id).ToArray();
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
        return videoStreams.Any(s => s.AuthorId != ownAuthor?.Id);
    }

    public ValueTask<bool> IsVideoStreamingEnabled(CancellationToken cancellationToken)
        => Features.IsVideoStreamingEnabled(cancellationToken);

    private void SetWatching(ChatId? chatId)
    {
        if (_watchingChatId.Value == chatId)
            return;
        _watchingChatId.Value = chatId;
        _isVideoPanelCollapsed.Value = false; // Reset collapsed state on watching change
        // Ensure listening is on when starting to watch
        if (chatId is not null)
            _ = ChatAudioUI.SetListeningState(chatId, true);
    }

    private void StartVideoStreaming(ChatId chatId, string? cameraDeviceId = null, bool isBackgroundBlurEnabled = false)
    {
        _recordingChatId.Value = chatId;
        _lastRecordingChatId.Value = chatId;
        _selectedCameraDeviceId.Value = cameraDeviceId;
        _isBackgroundBlurEnabled.Value = isBackgroundBlurEnabled;
        _isScreencasting.Value = false;
        OpenVideoPanel(chatId);
    }
}

// ReSharper disable once ClassNeverInstantiated.Global — instantiated via JS interop deserialization
public sealed record VideoDevice(string DeviceId, string Label);
