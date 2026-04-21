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
    // Centralized video state — webcam and screencast are tracked independently
    // so an author can stream both at the same time.
    private readonly MutableState<ChatId?> _recordingChatId;        // webcam target chat
    private readonly MutableState<ChatId?> _screencastChatId;       // screencast target chat
    private readonly MutableState<ChatId?> _lastRecordingChatId;
    private readonly MutableState<string?> _selectedCameraDeviceId;
    private readonly MutableState<bool> _isBackgroundBlurEnabled;
    private readonly MutableState<string?> _errorMessage;

    // Tracks which chat the user is currently watching video in (in-memory, resets on reload)
    private readonly MutableState<ChatId?> _watchingChatId;

    // UI-only: hides video panel without affecting watching/recording state
    private readonly MutableState<bool> _isVideoPanelCollapsed;

    // Set when a remote stream completes normally (sender intentionally ended).
    // Consumed by VideoPanelContent to suppress "Connecting..." overlay.
    private volatile int _remoteStreamEndedIntentionally;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    public ILiveVideoStreams LiveVideoStreams => field ??= Services.GetRequiredService<ILiveVideoStreams>();

    public ChatVideoUI(AppUIHub hub) : base(hub)
    {
        _recordingChatId = StateFactory.NewMutable((ChatId?)null);
        _screencastChatId = StateFactory.NewMutable((ChatId?)null);
        _lastRecordingChatId = StateFactory.NewMutable((ChatId?)null);
        _selectedCameraDeviceId = StateFactory.NewMutable((string?)null);
        _isBackgroundBlurEnabled = StateFactory.NewMutable(false);
        _errorMessage = StateFactory.NewMutable((string?)null);
        _watchingChatId = StateFactory.NewMutable((ChatId?)null);
        _isVideoPanelCollapsed = StateFactory.NewMutable(false);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    // Core state accessors

    /// <summary>
    /// Primary stream kind for a single-value UI surface. Screencast takes precedence
    /// when both are active. Prefer <see cref="IsOwnRecording"/> / <see cref="IsOwnScreencasting"/>
    /// for independent checks.
    /// </summary>
    [ComputeMethod]
    public virtual async Task<StreamKind?> GetOwnStreamKind(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var isVideoEnabled = await IsVideoStreamingEnabled(cancellationToken).ConfigureAwait(false);
        if (!isVideoEnabled)
            return null;

        if (await IsOwnScreencasting(chatId, cancellationToken).ConfigureAwait(false))
            return StreamKind.Screencast;
        if (await IsOwnRecording(chatId, cancellationToken).ConfigureAwait(false))
            return StreamKind.Webcam;
        return null;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsOwnRecording(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var isVideoEnabled = await IsVideoStreamingEnabled(cancellationToken).ConfigureAwait(false);
        if (!isVideoEnabled)
            return false;

        var recordingChatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        return recordingChatId == chatId;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsOwnScreencasting(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var isVideoEnabled = await IsVideoStreamingEnabled(cancellationToken).ConfigureAwait(false);
        if (!isVideoEnabled)
            return false;

        var screencastChatId = await _screencastChatId.Use(cancellationToken).ConfigureAwait(false);
        return screencastChatId == chatId;
    }

    [ComputeMethod]
    public virtual async Task<string?> GetLastVideoRecorderError(CancellationToken cancellationToken = default)
        => await _errorMessage.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<ChatId?> GetWatchingChatId(CancellationToken cancellationToken = default)
        => await _watchingChatId.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> IsWatching(ChatId chatId, CancellationToken cancellationToken = default)
        => await GetWatchingChatId(cancellationToken).ConfigureAwait(false) == chatId;

    [ComputeMethod]
    public virtual async Task<bool> GetIsVideoPanelCollapsed(CancellationToken cancellationToken = default)
        => await _isVideoPanelCollapsed.Use(cancellationToken).ConfigureAwait(false);

    // State mutators

    /// <summary>
    /// Stops all of the current user's outgoing streams (webcam + screencast).
    /// Used on hang-up.
    /// </summary>
    public void StopStreaming()
    {
        _recordingChatId.Value = null;
        _screencastChatId.Value = null;
    }

    /// <summary>
    /// Stops the webcam stream only. Screencast (if any) keeps running.
    /// </summary>
    public void StopRecording()
        => _recordingChatId.Value = null;

    /// <summary>
    /// Stops the screencast stream only. Webcam (if any) keeps running.
    /// </summary>
    public void StopScreenCasting()
        => _screencastChatId.Value = null;

    public void StartScreenCasting(ChatId chatId)
    {
        // Additive: does not stop webcam.
        _screencastChatId.Value = chatId;
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

    /// <summary>
    /// Called by VideoTrackPlayer when a remote stream ends normally (no error).
    /// </summary>
    public void NotifyRemoteStreamEndedIntentionally()
        => Interlocked.Exchange(ref _remoteStreamEndedIntentionally, 1);

    /// <summary>
    /// Atomically reads and resets the intentional-end flag.
    /// </summary>
    public bool ConsumeRemoteStreamEndedIntentionally()
        => Interlocked.Exchange(ref _remoteStreamEndedIntentionally, 0) != 0;

    public void ResumeVideoStreaming(ChatId chatId)
    {
        // Resume recording without overwriting camera/blur settings preserved from the previous recording
        _recordingChatId.Value = chatId;
        OpenVideoPanel(chatId);
    }

    // JS callback handlers (called from VideoPanel)

    public void OnRecordingStarted(ChatId chatId, StreamKind kind)
        // Clear any previous error (e.g. the user cycled past a failing camera
        // and landed on a working one) so VideoStreamingPreview drops the overlay.
        => _errorMessage.Value = null;

    public void OnRecordingStopped(StreamKind kind)
    {
        if (kind == StreamKind.Screencast)
            StopScreenCasting();
        else
            StopRecording();
    }

    public void OnRecordingError(string error, StreamKind kind)
        // Keep the recording session alive: the user can cycle cameras to recover
        // (see VideoRecorder.switchCamera — it restarts from the interrupted state).
        // VideoStreamingPreview shows the message via GetLastVideoRecorderError.
        => _errorMessage.Value = error;

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
        }
    }

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
        OpenVideoPanel(chatId);
    }
}

// ReSharper disable once ClassNeverInstantiated.Global — instantiated via JS interop deserialization
public sealed record VideoDevice(string DeviceId, string Label);
