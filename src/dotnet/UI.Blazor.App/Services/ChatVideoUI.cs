using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Components.VideoPanel;
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
    private static readonly TimeSpan LocallyEndedRemoteStreamRetention = TimeSpan.FromMinutes(3);

    // Centralized video state — camera and screencast are tracked independently
    // so an author can stream both at the same time.
    private readonly MutableState<ChatId?> _recordingChatId;        // camera target chat
    private readonly MutableState<ChatId?> _screenCastChatId;       // screencast target chat
    private readonly MutableState<ChatId?> _lastRecordingChatId;
    private readonly MutableState<bool> _isBackgroundBlurEnabled;
    private readonly MutableState<string?> _cameraErrorMessage;
    private readonly MutableState<string?> _screenCastErrorMessage;

    // Tracks which chat the user is currently watching video in (in-memory, resets on reload)
    private readonly MutableState<ChatId?> _watchingChatId;

    // UI-only: hides video panel without affecting watching/recording state
    private readonly MutableState<bool> _isVideoPanelCollapsed;
    private readonly MutableState<bool> _isVideoPanelHidden;
    private readonly MutableState<bool> _isVideoPanelExpanded;
    private readonly MutableState<bool> _isVideoPanelEqualLayout;
    private readonly MutableState<bool?> _isVideoPanelChatVisible;

    // Set when a remote stream completes normally (sender intentionally ended).
    // Consumed by VideoPanelContent to suppress "Connecting..." overlay.
    private volatile int _remoteStreamEndedSuccessfully;
    private readonly ConcurrentDictionary<string, CpuTimestamp> _locallyEndedRemoteStreams = new(StringComparer.Ordinal);

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
    private AudioRecorder AudioRecorder => Hub.AudioRecorder;
    private CameraUI CameraUI => Hub.CameraUI;
    private BrowserInfo BrowserInfo => Hub.BrowserInfo;

    public ChatVideoUI(AppUIHub hub) : base(hub)
    {
        _recordingChatId = StateFactory.NewMutable((ChatId?)null);
        _screenCastChatId = StateFactory.NewMutable((ChatId?)null);
        _lastRecordingChatId = StateFactory.NewMutable((ChatId?)null);
        _isBackgroundBlurEnabled = StateFactory.NewMutable(false);
        _cameraErrorMessage = StateFactory.NewMutable((string?)null);
        _screenCastErrorMessage = StateFactory.NewMutable((string?)null);
        _watchingChatId = StateFactory.NewMutable((ChatId?)null);
        _isVideoPanelCollapsed = StateFactory.NewMutable(false);
        _isVideoPanelHidden = StateFactory.NewMutable(false);
        _isVideoPanelExpanded = StateFactory.NewMutable(false);
        _isVideoPanelEqualLayout = StateFactory.NewMutable(false);
        _isVideoPanelChatVisible = StateFactory.NewMutable((bool?)null);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    // Core state accessors

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
    public virtual async Task<ChatId?> GetWatchingChatId(CancellationToken cancellationToken = default)
        => await _watchingChatId.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> IsWatching(ChatId chatId, CancellationToken cancellationToken = default)
        => await GetWatchingChatId(cancellationToken).ConfigureAwait(false) == chatId;

    [ComputeMethod]
    public virtual async Task<bool> GetIsVideoPanelCollapsed(CancellationToken cancellationToken = default)
        => await _isVideoPanelCollapsed.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> GetIsVideoPanelHidden(CancellationToken cancellationToken = default)
        => await _isVideoPanelHidden.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> GetIsVideoPanelExpanded(CancellationToken cancellationToken = default)
        => await _isVideoPanelExpanded.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> GetIsVideoPanelEqualLayout(CancellationToken cancellationToken = default)
        => await _isVideoPanelEqualLayout.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool?> GetIsVideoPanelChatVisible(CancellationToken cancellationToken = default)
        => await _isVideoPanelChatVisible.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<VideoPanelMode> GetVideoPanelMode(CancellationToken cancellationToken = default)
    {
        if (await _isVideoPanelHidden.Use(cancellationToken).ConfigureAwait(false))
            return VideoPanelMode.Hidden;
        if (await _isVideoPanelExpanded.Use(cancellationToken).ConfigureAwait(false))
            return VideoPanelMode.Expanded;
        if (await _isVideoPanelCollapsed.Use(cancellationToken).ConfigureAwait(false))
            return VideoPanelMode.Collapsed;
        return VideoPanelMode.Inline;
    }

    [ComputeMethod]
    public virtual async Task<VideoPanelActions> GetVideoPanelActions(
        ChatId chatId, bool isNarrow, CancellationToken cancellationToken = default)
    {
        var mode = await GetVideoPanelMode(cancellationToken).ConfigureAwait(false);
        var isVideoAvailable = await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false);
        var settings = await LocalSettings.LocalAppSettings().Get(cancellationToken).ConfigureAwait(false);
        return new VideoPanelActions(
            Mode: mode,
            IsNarrow: isNarrow,
            CanToggleFullscreen: true,
            CanToggleIsland: true,
            CanMinimize: !isNarrow && mode != VideoPanelMode.Hidden,
            CanSwitchCamera: isNarrow && isVideoAvailable,
            CanToggleVideo: isVideoAvailable,
            CanToggleScreenCast: !isNarrow && !BrowserInfo.IsMobile && isVideoAvailable,
            CanToggleChatPanel: !isNarrow && mode == VideoPanelMode.Expanded,
            CanShowDiagnostics: settings.IsVideoDiagnosticsEnabledOrDefault || HostInfo.IsDevelopmentInstance,
            CanShowVoiceSettings: true,
            CanShowVideoSettings: isVideoAvailable);
    }

    // State mutators

    public void OpenVideoPanel(ChatId chatId)
        => _ = OpenVideoPanelInternal(chatId);

    public void CloseVideoPanel()
        => SetWatching(null);

    public void SetVideoPanelCollapsed(bool collapsed)
    {
        _isVideoPanelCollapsed.Value = collapsed;
        if (collapsed) {
            _isVideoPanelHidden.Value = false;
            _isVideoPanelExpanded.Value = false;
        }
    }

    public void SetVideoPanelHidden(bool hidden)
    {
        _isVideoPanelHidden.Value = hidden;
        if (hidden) {
            _isVideoPanelCollapsed.Value = false;
            _isVideoPanelExpanded.Value = false;
        }
    }

    public void SetVideoPanelExpanded(bool expanded)
    {
        _isVideoPanelExpanded.Value = expanded;
        if (expanded) {
            _isVideoPanelCollapsed.Value = false;
            _isVideoPanelHidden.Value = false;
        }
    }

    public void SetVideoPanelEqualLayout(bool equal)
        => _isVideoPanelEqualLayout.Value = equal;

    public void SetVideoPanelChatVisible(bool? visible)
        => _isVideoPanelChatVisible.Value = visible;

    public bool HasJoinedVideoSession(ChatId chatId)
        => _watchingChatId.Value == chatId && _lastRecordingChatId.Value == chatId;

    public void ToggleVideoCapture(ChatId chatId)
    {
        var isOwnRecording = _recordingChatId.Value == chatId;
        if (isOwnRecording) {
            StopRecording();
            return;
        }
        if (HasJoinedVideoSession(chatId))
            ResumeVideoStreaming(chatId);
        else
            JoinVideoSession(chatId);
    }

    public void ToggleScreenCast(ChatId chatId)
    {
        var isOwnScreenCasting = _screenCastChatId.Value == chatId;
        if (isOwnScreenCasting)
            StopScreenCasting();
        else
            StartScreenCasting(chatId);
    }

    public void ShowDiagnostics(ChatId chatId)
        => _ = ModalUI.Show(new VideoDiagnosticsModal.Model(chatId));

    public void ShowVoiceSettings(ChatId chatId)
        => _ = ModalUI.Show(new VoiceSettingsModal.Model(chatId));

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

            var chatContext = new ChatContext(Hub, chat);
            var model = new JoinVideoCallModal.Model(chatContext, JoinVideoCallModal.VideoCallMode.Join);
            var modeRef = await ModalUI.Show(model, CancellationToken.None).ConfigureAwait(true);
            await modeRef.WhenClosed.ConfigureAwait(true);
            if (!model.IsConfirmed)
                return;

            // Apply the mic choice from the preview modal: start recording when
            // the user left mic on, stop it when they turned it off while it was live.
            if (model.IsMicOn) {
                if (await AudioRecorder.MicrophonePermission.CheckOrRequest(cancellationToken).ConfigureAwait(true))
                    await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(true);
            }
            else if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(true) == chatId)
                await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(true);

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
                var chatContext = new ChatContext(Hub, chat);
                var model = new JoinVideoCallModal.Model(chatContext, JoinVideoCallModal.VideoCallMode.Settings);
                var modeRef = await ModalUI.Show(model, CancellationToken.None).ConfigureAwait(true);
                await modeRef.WhenClosed.ConfigureAwait(true);
            }
            finally {
                SuspendOwnStreamingPreview?.Invoke(false);
            }
        }
    }

    // Private methods

    private void SetWatching(ChatId? chatId)
    {
        if (_watchingChatId.Value == chatId)
            return;
        _watchingChatId.Value = chatId;
        // Reset transient view state only when opening the panel; on close we
        // leave it as-is so the panel stays in its current visual mode while
        // it unmounts (otherwise it would snap to inline position first).
        if (chatId is not null) {
            _isVideoPanelCollapsed.Value = false;
            _isVideoPanelHidden.Value = false;
            _isVideoPanelExpanded.Value = false;
            _isVideoPanelChatVisible.Value = null;
            _ = ChatAudioUI.SetListeningState(chatId, true);
        }
    }

    private async Task OpenVideoPanelInternal(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false))
            return;

        SetWatching(chatId);
    }

}

// ReSharper disable once ClassNeverInstantiated.Global — instantiated via JS interop deserialization
public sealed record VideoDevice(string DeviceId, string Label, string? Facing = null)
{
    public bool IsFront => Facing == "user";
    public bool IsBack => Facing == "environment";
}

public enum VideoPanelMode
{
    Inline,
    Expanded,
    Collapsed,
    Hidden,
}

public sealed record VideoPanelActions(
    VideoPanelMode Mode,
    bool IsNarrow,
    bool CanToggleFullscreen,
    bool CanToggleIsland,
    bool CanMinimize,
    bool CanSwitchCamera,
    bool CanToggleVideo,
    bool CanToggleScreenCast,
    bool CanToggleChatPanel,
    bool CanShowDiagnostics,
    bool CanShowVoiceSettings,
    bool CanShowVideoSettings);
