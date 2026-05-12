using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Components.VideoPanel;
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
    private CameraUI CameraUI => Hub.CameraUI;

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

    // State mutators

    public void OpenVideoPanel(ChatId chatId)
        => _ = OpenVideoPanelInternal(chatId);

    public void CloseVideoPanel()
        => SetWatching(null);

    public void SetVideoPanelCollapsed(bool collapsed)
    {
        _isVideoPanelCollapsed.Value = collapsed;
        if (collapsed)
            _isVideoPanelHidden.Value = false;
    }

    public void SetVideoPanelHidden(bool hidden)
    {
        _isVideoPanelHidden.Value = hidden;
        if (hidden)
            _isVideoPanelCollapsed.Value = false;
    }

    public bool HasJoinedVideoSession(ChatId chatId)
        => _watchingChatId.Value == chatId && _lastRecordingChatId.Value == chatId;

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

    // Private methods

    private void SetWatching(ChatId? chatId)
    {
        if (_watchingChatId.Value == chatId)
            return;
        _watchingChatId.Value = chatId;
        _isVideoPanelCollapsed.Value = false; // Reset collapsed state on watching change
        _isVideoPanelHidden.Value = false;
        // Ensure listening is on when starting to watch
        if (chatId is not null)
            _ = ChatAudioUI.SetListeningState(chatId, true);
    }

    private async Task OpenVideoPanelInternal(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false))
            return;

        SetWatching(chatId);
    }

}

// ReSharper disable once ClassNeverInstantiated.Global — instantiated via JS interop deserialization
public sealed record VideoDevice(string DeviceId, string Label);
