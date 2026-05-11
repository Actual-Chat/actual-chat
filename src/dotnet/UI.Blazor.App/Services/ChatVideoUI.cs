using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Components.VideoPanel;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.Video;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Provides reactive access to video stream data for the current chat.
/// Player lifecycle management is handled by VideoTrackPlayer components.
/// State is in-memory only (not persisted) since video recording can't survive page refresh.
/// </summary>
public partial class ChatVideoUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    // Centralized video state — camera and screencast are tracked independently
    // so an author can stream both at the same time.
    private readonly MutableState<ChatId?> _recordingChatId;        // camera target chat
    private readonly MutableState<ChatId?> _screenCastChatId;       // screencast target chat
    private readonly MutableState<ChatId?> _lastRecordingChatId;
    private readonly MutableState<string?> _selectedCameraDeviceId;
    private readonly MutableState<bool> _isBackgroundBlurEnabled;
    private readonly MutableState<bool> _isCameraMirrored;
    private readonly MutableState<string?> _cameraErrorMessage;
    private readonly MutableState<string?> _screenCastErrorMessage;

    // Tracks which chat the user is currently watching video in (in-memory, resets on reload)
    private readonly MutableState<ChatId?> _watchingChatId;

    // UI-only: hides video panel without affecting watching/recording state
    private readonly MutableState<bool> _isVideoPanelCollapsed;

    // Set when a remote stream completes normally (sender intentionally ended).
    // Consumed by VideoPanelContent to suppress "Connecting..." overlay.
    private volatile int _remoteStreamEndedIntentionally;

    /// <summary>
    /// Raised to ask <see cref="VideoStreamingPreview"/> consumers to pause (true) /
    /// resume (false) their local preview rendering while something else owns the
    /// preview canvas — e.g. the Settings-mode JoinVideoCallModal. Fires on the
    /// Blazor dispatcher; subscribers can call into JS synchronously from the handler.
    /// </summary>
    public event Action<bool>? SuspendOwnStreamingPreview;

    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    private ILiveVideoStreams LiveVideoStreams => Hub.LiveVideoStreams;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;

    public ChatVideoUI(AppUIHub hub) : base(hub)
    {
        _recordingChatId = StateFactory.NewMutable((ChatId?)null);
        _screenCastChatId = StateFactory.NewMutable((ChatId?)null);
        _lastRecordingChatId = StateFactory.NewMutable((ChatId?)null);
        _selectedCameraDeviceId = StateFactory.NewMutable((string?)null);
        _isBackgroundBlurEnabled = StateFactory.NewMutable(false);
        _isCameraMirrored = StateFactory.NewMutable(true);
        _cameraErrorMessage = StateFactory.NewMutable((string?)null);
        _screenCastErrorMessage = StateFactory.NewMutable((string?)null);
        _watchingChatId = StateFactory.NewMutable((ChatId?)null);
        _isVideoPanelCollapsed = StateFactory.NewMutable(false);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    // Core state accessors

    /// <summary>
    /// Primary stream kind for a single-value UI surface. ScreenCast takes precedence
    /// when both are active. Prefer <see cref="IsOwnCameraRecording"/> / <see cref="IsOwnScreenCasting"/>
    /// for independent checks.
    /// </summary>
    [ComputeMethod]
    public virtual async Task<VideoSourceKind?> GetOwnSourceKind(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (await IsOwnScreenCasting(chatId, cancellationToken).ConfigureAwait(false))
            return VideoSourceKind.ScreenCast;
        if (await IsOwnCameraRecording(chatId, cancellationToken).ConfigureAwait(false))
            return VideoSourceKind.Camera;
        return null;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsVideoAvailable(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        return IsVideoAvailableNonComputed(chat);
    }

#pragma warning disable CA1822 // Non-computed fast path for an already-loaded chat
    public bool IsVideoAvailableNonComputed(ActualChat.Chat.Chat? chat)
        => chat is { HasSingleAuthor: false };
#pragma warning restore CA1822

    [ComputeMethod]
    public virtual async Task<bool> IsVideoEnabled(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false))
            return false;

#if false
        if (chatId.Kind == ChatKind.Peer)
            return true;

        var account = await Hub.AccountUI.OwnAccount.Use(cancellationToken).ConfigureAwait(false);
        return account.IsAdmin;
#else
        // NOTE(AY): Let's try to enable it in all chats
        return true;
#endif
    }

    [ComputeMethod]
    public virtual async Task<bool> IsOwnCameraRecording(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoEnabled(chatId, cancellationToken).ConfigureAwait(false))
            return false;

        var recordingChatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        return recordingChatId == chatId;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsOwnScreenCasting(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoEnabled(chatId, cancellationToken).ConfigureAwait(false))
            return false;

        var screenCastChatId = await _screenCastChatId.Use(cancellationToken).ConfigureAwait(false);
        return screenCastChatId == chatId;
    }

    [ComputeMethod]
    public virtual async Task<string?> GetLastVideoRecorderError(CancellationToken cancellationToken = default)
        => await _cameraErrorMessage.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<string?> GetLastVideoRecorderError(VideoSourceKind kind, CancellationToken cancellationToken = default)
        => await GetErrorState(kind).Use(cancellationToken).ConfigureAwait(false);

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
    /// Stops all of the current user's outgoing streams (camera + screencast).
    /// Used on hang-up.
    /// </summary>
    public void StopStreaming()
    {
        _recordingChatId.Value = null;
        _screenCastChatId.Value = null;
        ClearRecordingError(VideoSourceKind.Camera);
        ClearRecordingError(VideoSourceKind.ScreenCast);
    }

    /// <summary>
    /// Stops the camera stream only. ScreenCast (if any) keeps running.
    /// </summary>
    public void StopRecording()
    {
        _recordingChatId.Value = null;
        ClearRecordingError(VideoSourceKind.Camera);
    }

    /// <summary>
    /// Stops the screencast stream only. Camera (if any) keeps running.
    /// </summary>
    public void StopScreenCasting()
    {
        _screenCastChatId.Value = null;
        ClearRecordingError(VideoSourceKind.ScreenCast);
    }

    public void StartScreenCasting(ChatId chatId)
        => _ = StartScreenCastingInternal(chatId);

    private async Task StartScreenCastingInternal(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoEnabled(chatId, cancellationToken).ConfigureAwait(false))
            return;

        if (_screenCastChatId.Value == chatId) {
            await ShowScreenCastAlreadyActiveModal(cancellationToken).ConfigureAwait(true);
            return;
        }

        try {
            var activeStreams = await GetActiveVideoStreams(chatId, cancellationToken).ConfigureAwait(true);
            if (activeStreams.Any(s => s.SourceKind == VideoSourceKind.ScreenCast)) {
                await ShowScreenCastAlreadyActiveModal(cancellationToken).ConfigureAwait(true);
                return;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to check active screencast before starting screen share");
        }

        // Additive: does not stop camera.
        _screenCastChatId.Value = chatId;
        OpenVideoPanel(chatId);
    }

    public void SetSelectedCamera(string? cameraDeviceId)
        => _selectedCameraDeviceId.Value = cameraDeviceId;

    public void SetBackgroundBlur(bool enabled)
        => _isBackgroundBlurEnabled.Value = enabled;

    [ComputeMethod]
    public virtual async Task<bool> GetIsCameraMirrored(CancellationToken cancellationToken = default)
        => await _isCameraMirrored.Use(cancellationToken).ConfigureAwait(false);

    // Last track settings reported by the active camera recorder. Plain fields —
    // only touched from the Blazor dispatcher (JS callback + UI consumers).
    public string? LastCameraDeviceId { get; private set; }
    public string? LastCameraFacingMode { get; private set; }

    internal void OnCameraTrackSettings(string? deviceId, string? facingMode)
    {
        // Called by the active camera recorder after each track acquisition
        // (start or camera switch). Resolves the effective mirror state from
        // per-camera overrides so the live self-preview reflects the right
        // camera regardless of how the stream was started.
        LastCameraDeviceId = deviceId;
        LastCameraFacingMode = facingMode;
        _ = ApplyAsync();
        return;

        async Task ApplyAsync() {
            var settings = await LocalSettings.LocalAppSettings().Get().ConfigureAwait(false);
            _isCameraMirrored.Value = settings
                .ResolveIsCameraMirrored(deviceId, facingMode, Hub.BrowserInfo.IsMobile);
        }
    }

    // Called by JoinVideoCallModal after it persists a user's mirror choice —
    // forces the live preview to re-resolve against the now-updated override
    // without waiting for the next camera (re)acquisition.
    public void ReapplyCameraMirror()
        => OnCameraTrackSettings(LastCameraDeviceId, LastCameraFacingMode);

    public void CloseVideoPanel()
        => SetWatching(null);

    public void OpenVideoPanel(ChatId chatId)
        => _ = OpenVideoPanelInternal(chatId);

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
        => _ = ResumeVideoStreamingInternal(chatId);

    // JS callback handlers (called from VideoPanel)

    public void OnRecordingStarted(ChatId chatId, VideoSourceKind kind)
    {
        // Clear any previous error (e.g. the user cycled past a failing camera
        // and landed on a working one) so VideoStreamingPreview drops the overlay.
        ClearRecordingError(kind);
        Hub.AnalyticEvents.RaiseVideoStreamStarted(kind);
    }

    public void OnRecordingStopped(VideoSourceKind kind)
    {
        if (kind == VideoSourceKind.ScreenCast)
            StopScreenCasting();
        else
            StopRecording();
    }

    public void OnRecordingError(string error, VideoSourceKind kind)
    {
        if (kind == VideoSourceKind.ScreenCast && IsScreenCastAlreadyActiveError(error)) {
            ClearRecordingError(VideoSourceKind.ScreenCast);
            _screenCastChatId.Value = null;
            _ = ShowScreenCastAlreadyActiveModal(CancellationToken.None);
            return;
        }

        GetErrorState(kind).Value = error;
        // Camera keeps the session alive — the user can cycle cameras to recover
        // (see VideoRecorder.switchCamera — it restarts from the interrupted state).
        // ScreenCast has no such retry path: a failed getDisplayMedia (user cancel,
        // permission denied) means the user doesn't want to share, so turn the
        // toggle off by clearing the intent.
        if (kind == VideoSourceKind.ScreenCast)
            _screenCastChatId.Value = null;
    }

    // Device enumeration

    public async Task<VideoDevice[]> EnumerateVideoDevices(bool includeAll = false)
    {
        try {
            var jsMethod = $"{BlazorUIAppModule.ImportName}.VideoRecorder.enumerateDevices";
            return await JS.InvokeAsync<VideoDevice[]>(jsMethod, includeAll).ConfigureAwait(false);
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
            if (chat is null || !IsVideoAvailableNonComputed(chat))
                return;

            var model = new JoinVideoCallModal.Model(chat, JoinVideoCallModal.VideoCallMode.Join);
            var modeRef = await ModalUI.Show(model, CancellationToken.None).ConfigureAwait(true);
            await modeRef.WhenClosed.ConfigureAwait(true);
            if (!model.IsConfirmed)
                return;

            if (!model.IsVideoOn) {
                // Viewer join: only opening the panel — no recording / streaming.
                // The Submit button is disabled in this branch unless remote
                // streams are already there, so we can rely on something to watch.
                OpenVideoPanel(chatId);
                return;
            }

            await LocalSettings.LocalAppSettings()
                .Update(s => s with { SelectedCameraDeviceId = model.SelectedDeviceId }, cancellationToken).ConfigureAwait(true);
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
            if (chat is null || !IsVideoAvailableNonComputed(chat))
                return;

            // Freeze the VideoPanel's self-preview before the modal even opens so
            // its canvas doesn't keep rendering into the frame the modal is about
            // to take over. `finally` makes sure we always resume, even if Show
            // throws or the modal never opens.
            SuspendOwnStreamingPreview?.Invoke(true);
            try {
                var model = new JoinVideoCallModal.Model(chat, JoinVideoCallModal.VideoCallMode.Settings);
                var modeRef = await ModalUI.Show(model, CancellationToken.None).ConfigureAwait(true);
                await modeRef.WhenClosed.ConfigureAwait(true);
            }
            finally {
                SuspendOwnStreamingPreview?.Invoke(false);
            }
        }
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<VideoStreamInfo>> GetActiveVideoStreams(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoEnabled(chatId, cancellationToken).ConfigureAwait(false))
            return [];

        return await LiveVideoStreams
            .List(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoEnabled(chatId, cancellationToken).ConfigureAwait(false))
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

    private async Task ShowScreenCastAlreadyActiveModal(CancellationToken cancellationToken)
        => await ModalUI.Show(new ScreenCastAlreadyActiveModal.Model(), cancellationToken).ConfigureAwait(true);

    private MutableState<string?> GetErrorState(VideoSourceKind kind)
        => kind == VideoSourceKind.ScreenCast ? _screenCastErrorMessage : _cameraErrorMessage;

    private void ClearRecordingError(VideoSourceKind kind)
        => GetErrorState(kind).Value = null;

    private async Task OpenVideoPanelInternal(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoEnabled(chatId, cancellationToken).ConfigureAwait(false))
            return;

        SetWatching(chatId);
    }

    private async Task ResumeVideoStreamingInternal(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoEnabled(chatId, cancellationToken).ConfigureAwait(false))
            return;

        // Resume recording without overwriting camera/blur settings preserved from the previous recording
        _recordingChatId.Value = chatId;
        await OpenVideoPanelInternal(chatId, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsScreenCastAlreadyActiveError(string error)
        => error.Contains("Another screencast is already active", StringComparison.OrdinalIgnoreCase)
            || error.Contains("screen sharing is already active", StringComparison.OrdinalIgnoreCase)
            || error.Contains("screen share is already active", StringComparison.OrdinalIgnoreCase);
}

// ReSharper disable once ClassNeverInstantiated.Global — instantiated via JS interop deserialization
public sealed record VideoDevice(string DeviceId, string Label);
