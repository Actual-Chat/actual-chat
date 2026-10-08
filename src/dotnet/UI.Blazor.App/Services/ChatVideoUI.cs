using ActualChat.Live;
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

    // See ChatVideoUI.MemberRegistration.cs
    private readonly MutableState<int> _memberRegistrationEpoch;
    private DotNetObjectReference<ChatVideoUI>? _registrationHookRef;

    // UI-only video panel view options; the panel's mode lives in ChatActivityUI
    private readonly MutableState<bool> _isVideoPanelEqualLayout;
    private readonly MutableState<bool?> _isVideoPanelChatVisible;

    // Set when a remote stream completes normally (sender intentionally ended).
    // Consumed by VideoStage to suppress "Connecting..." overlay.
    private int _remoteStreamEndedSuccessfully;

    // Raised to ask VideoStreamingPreview consumers to pause (true) / resume (false)
    // their local preview rendering while something else owns the preview canvas —
    // e.g. the Settings-mode JoinVideoCallModal. Fires on the Blazor dispatcher;
    // subscribers can call into JS synchronously from the handler.
    public event Action<bool>? SuspendOwnStreamingPreview;

    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    private ILiveVideoStreams LiveVideoStreams => Hub.LiveVideoStreams;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    // TODO: ChatActivityUI can reference ChatVideoUI, but not reverse
    private ChatActivityUI ChatActivityUI => Hub.ChatActivityUI;
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
        _memberRegistrationEpoch = StateFactory.NewMutable(0);
        _isVideoPanelEqualLayout = StateFactory.NewMutable(false);
        _isVideoPanelChatVisible = StateFactory.NewMutable((bool?)null);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    // Core state accessors

    [ComputeMethod(ConsolidationDelay = 0.2)]
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

    // The non-reactive form of GetWatchingChatId, for callbacks that can't await.
    public ChatId? WatchingChatId => _watchingChatId.Value;

    [ComputeMethod]
    public virtual async Task<bool> IsWatching(ChatId chatId, CancellationToken cancellationToken = default)
        => await GetWatchingChatId(cancellationToken).ConfigureAwait(false) == chatId;

    [ComputeMethod]
    public virtual async Task<VisualActivityPanelMode> GetShownPanelMode(
        ChatId chatId, CancellationToken cancellationToken = default)
    {
        var mode = await ChatActivityUI.GetPanelMode(chatId, cancellationToken).ConfigureAwait(false);
        if (mode != VisualActivityPanelMode.Inline)
            return mode;
        if (!await IsWatching(chatId, cancellationToken).ConfigureAwait(false))
            return mode;

        var call = await Hub.CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is not { Phase: CallPhase.Active } || call.ChatId != chatId)
            return mode;

        // The chat list of a narrow screen covers the chat without unselecting it
        var selectedChatId = await Hub.ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        var isOnChatPage = selectedChatId == chatId
            && await Hub.PanelsUI.Middle.IsVisible(cancellationToken).ConfigureAwait(false);
        return DecideCallVideoPanelMode(mode, isOnChatPage);
    }

    internal static VisualActivityPanelMode DecideCallVideoPanelMode(VisualActivityPanelMode mode, bool isOnChatPage)
        // An inline video of a call stays in sight off its chat's page, floating there. The mode the
        // user picked isn't touched, so it is inline again on the return.
        => mode == VisualActivityPanelMode.Inline && !isOnChatPage
            ? VisualActivityPanelMode.Collapsed
            : mode;

    [ComputeMethod]
    public virtual async Task<bool> GetIsVideoPanelEqualLayout(CancellationToken cancellationToken = default)
        => await _isVideoPanelEqualLayout.Use(cancellationToken).ConfigureAwait(false);

    // The side chat docks only on a genuinely wide desktop; below Large the full-screen video takes the
    // phone's control set: no side chat, no chat toggle and no screen share.
    [ComputeMethod]
    public virtual async Task<bool> IsDesktopLayout(CancellationToken cancellationToken = default)
    {
        var screenSize = await BrowserInfo.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        return screenSize >= ScreenSize.Large && !BrowserInfo.IsMobile;
    }

    [ComputeMethod]
    public virtual async Task<bool> GetIsVideoPanelChatVisible(CancellationToken cancellationToken = default)
    {
        if (!await IsDesktopLayout(cancellationToken).ConfigureAwait(false))
            return false;

        // Shown until the user hides it
        return await _isVideoPanelChatVisible.Use(cancellationToken).ConfigureAwait(false) ?? true;
    }

    [ComputeMethod]
    public virtual async Task<VisualActivityPanelMode> GetWatchingPanelMode(
        CancellationToken cancellationToken = default)
    {
        // The mode the watched chat's video really shows in — the one governing video playback
        var screen = await Hub.CallScreensUI.GetScreen(cancellationToken).ConfigureAwait(false);
        if (screen is not { HasVideo: true })
            return VisualActivityPanelMode.Inline;
        if (screen.Mode != VisualActivityPanelMode.Inline)
            return screen.Mode;

        // Inline, the video is a part of its chat's page, on the Call tab there. Anywhere else it is
        // out of sight, though still mounted - which is what Hidden is.
        var chatId = screen.ChatId;
        var selectedChatId = await Hub.ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        if (selectedChatId != chatId)
            return VisualActivityPanelMode.Hidden;

        var activity = await ChatActivityUI.Get(chatId, cancellationToken).ConfigureAwait(false);
        return activity.Tab == VisualActivityTab.Call
            ? VisualActivityPanelMode.Inline
            : VisualActivityPanelMode.Hidden;
    }

    [ComputeMethod]
    public virtual async Task<VideoPanelActions> GetVideoPanelActions(
        ChatId chatId, bool isNarrow, CancellationToken cancellationToken = default)
    {
        // The mode of the whole call screen, not of the panel alone: a call without video is
        // full-screen too, and has no video to float or hide.
        var screen = await Hub.CallScreensUI.GetScreen(cancellationToken).ConfigureAwait(false);
        var isOnScreen = screen?.ChatId == chatId;
        var mode = isOnScreen
            ? screen!.Mode
            : await ChatActivityUI.GetPanelMode(chatId, cancellationToken).ConfigureAwait(false);
        var hasVideo = isOnScreen && screen!.HasVideo;
        var isVideoAvailable = await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false);
        var isDiagnosticsEnabled = await IsDiagnosticsEnabled(cancellationToken).ConfigureAwait(false);
        var isDesktopLayout = await IsDesktopLayout(cancellationToken).ConfigureAwait(false);
        return new VideoPanelActions(
            Mode: mode,
            IsNarrow: isNarrow,
            CanToggleFullscreen: hasVideo,
            CanToggleIsland: hasVideo,
            CanMinimize: hasVideo && !isNarrow && mode != VisualActivityPanelMode.Hidden,
            CanSwitchCamera: isNarrow && isVideoAvailable,
            CanToggleVideo: isVideoAvailable,
            CanToggleScreenCast: !BrowserInfo.IsMobile && isVideoAvailable,
            CanToggleChatPanel: hasVideo && isDesktopLayout && mode == VisualActivityPanelMode.Expanded,
            CanShowDiagnostics: isDiagnosticsEnabled,
            CanShowVoiceSettings: true,
            CanShowVideoSettings: isVideoAvailable);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsDiagnosticsEnabled(CancellationToken cancellationToken = default)
    {
        // Dev instances only shift the default — an explicit setting always wins.
        var settings = await UserSettingsUI.UserAppSettings().Get(cancellationToken).ConfigureAwait(false);
        return settings.IsVideoDiagnosticsEnabled ?? HostInfo.IsDevelopmentInstance;
    }

    // State mutators

    public void OpenVideoPanel(ChatId chatId, bool isExpanded = false)
        => _ = OpenVideoPanelInternal(chatId, isExpanded);

    public void CloseVideoPanel()
        => SetWatching(null);

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

    public async Task StartVideoCapture(
        ChatId chatId, bool isExpanded = false, CancellationToken cancellationToken = default)
    {
        // No join preview: for a gesture that already said "video on", such as the system call UI's
        // video button. The camera and blur are the ones the last session saved.
        if (_recordingChatId.Value == chatId)
            return;

        var startTask = HasJoinedVideoSession(chatId)
            ? ResumeVideoStreamingInternal(chatId, cancellationToken)
            : StartVideoCaptureInternal(chatId, cancellationToken);
        await startTask.ConfigureAwait(false);
        if (isExpanded)
            await OpenVideoPanelInternal(chatId, true, cancellationToken).ConfigureAwait(false);
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

        async Task JoinInternal(CancellationToken cancellationToken = default) {
            var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat is null || !IsVideoAvailableNonComputed(chat))
                return;

            // On a call in this chat the camera just joins in, with the last session's camera and blur.
            // An open mic alone doesn't count: outside a call the preview is the only place to pick them.
            var isOnCall = Hub.CallUI.GetActiveCallNonComputed() is { Phase: CallPhase.Active } call
                && call.ChatId == chatId;
            if (isOnCall) {
                // A call's video opens full-screen - unless its panel is already up, in the mode the
                // user left it in.
                var isExpanded = _watchingChatId.Value != chatId;
                await StartVideoCapture(chatId, isExpanded, cancellationToken).ConfigureAwait(false);
                return;
            }

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
                .Update(s => s with { SelectedCameraDeviceId = model.SelectedDeviceId }, cancellationToken)
                .ConfigureAwait(true);
            StartVideoStreaming(chatId, model.SelectedDeviceId, model.IsBlurEnabled);
        }
    }

    public void ChangeVideoSessionSettings(ChatId chatId)
    {
        _ = ChangeInternal();
        return;

        async Task ChangeInternal() {
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
        if (chatId is { } id) {
            ChatActivityUI.SetPanelMode(id, VisualActivityPanelMode.Inline);
            _isVideoPanelChatVisible.Value = null;
            _ = ChatAudioUI.SetListeningState(id, true);
        }
    }

    private async Task OpenVideoPanelInternal(
        ChatId chatId, bool isExpanded = false, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false))
            return;

        SetWatching(chatId);
        // Set after the open, which resets the mode - and on a panel that is already up as well.
        if (isExpanded && _watchingChatId.Value == chatId)
            Hub.CallScreensUI.SetScreenMode(chatId, VisualActivityPanelMode.Expanded);
    }
}

// ReSharper disable once ClassNeverInstantiated.Global — instantiated via JS interop deserialization
public sealed record VideoDevice(string DeviceId, string Label, string? Facing = null)
{
    public bool IsFront => Facing == "user";
    public bool IsBack => Facing == "environment";
}

public sealed record VideoPanelActions(
    VisualActivityPanelMode Mode,
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
