using ActualChat.Audio;
using ActualChat.Kvas;
using ActualChat.UI.App.Services;
using ActualChat.UI.Blazor.App.Components.AudioPanel;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages audio listening and recording state for chats in the UI.
/// </summary>
public partial class ChatAudioUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized, IDebugAudioSync
{
    private static bool DebugMode => Constants.DebugMode.ChatAudioUI;

    private readonly MutableState<Moment?> _stopRecordingAt;
    private readonly MutableState<ImmutableDictionary<ChatId, Moment>> _stopListeningAtMap;
    private readonly MutableState<NextBeepState?> _nextBeep;
    private readonly AsyncTaskMethodBuilder _whenEnabledSource = AsyncTaskMethodBuilderExt.New();
    // Boxed because the CLR forbids volatile on Nullable<TimeSpan>; null means "no override".
    private volatile object? _recordingIdleDurationBox;
    private bool _isBeginTuneSuppressed;

    private IChats Chats => Hub.Chats;
    private LiveStreamUI LiveStreamUI => Hub.LiveStreamUI;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;
    private IAudioInitializer AudioInitializer => Hub.AudioInitializer;
    private AudioFocusUI AudioFocusUI => Hub.AudioFocusUI;
    private ChatEditorUI ChatEditorUI => Hub.ChatEditorUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private LanguageUI LanguageUI => Hub.LanguageUI;
    private UserActivityUI UserActivityUI => Hub.UserActivityUI;
    private InteractiveUI InteractiveUI => Hub.InteractiveUI;
    private DeviceAwakeUI DeviceAwakeUI => Hub.DeviceAwakeUI;
    private IncomingShareSuggestions? IncomingShareSuggestions { get; }
    private AudioSettings AudioSettings => Hub.AudioSettings;
    private AudioRecorder AudioRecorder => Hub.AudioRecorder;
    private Moment CpuNow => Clocks.CpuClock.Now;
    private Moment ServerNow => Clocks.ServerClock.Now;
    private new ILogger? DebugLog => DebugMode ? Log : null;

    public bool IsAudioSyncEnabled { get; set; } = true;
    public bool IsWalkieTalkieHeadless { get; set; }
    public SyncedState<UserReplaySettings> ReplaySettings { get; init; }
    public IState<ReplayState?> ReplayState => _replayState;
    public IState<Moment?> StopRecordingAt => _stopRecordingAt; // CPU time
    public IState<NextBeepState?> NextBeep => _nextBeep;
    public Task WhenEnabled => _whenEnabledSource.Task;

    public ChatAudioUI(AppUIHub hub) : base(hub)
    {
        IncomingShareSuggestions = hub.Services.GetService<IncomingShareSuggestions>();

        var type = GetType();
        var stateFactory = StateFactory;
        ReplaySettings = stateFactory.NewUserSettingsSynced(
            UserSettingsUI,
            UserReplaySettings.KvasKey,
            new UserReplaySettings(),
            updateDelayer: FixedDelayer.NextTick,
            category: StateCategories.Get(type, nameof(ReplaySettings)));
        Hub.RegisterDisposable(ReplaySettings);

        _stopRecordingAt = stateFactory.NewMutable((Moment?)null, StateCategories.Get(type, nameof(StopRecordingAt)));
        _stopListeningAtMap = stateFactory.NewMutable(
            ImmutableDictionary<ChatId, Moment>.Empty,
            StateCategories.Get(type, nameof(GetStopListeningAt)));
        _nextBeep = stateFactory.NewMutable((NextBeepState?)null, StateCategories.Get(type, nameof(NextBeep)));
        _replayState = stateFactory.NewMutable(
            (ReplayState?)null,
            StateCategories.Get(type, nameof(ReplayState)));
        _audioFocusRequester = new AudioFocusRequester(AudioFocusMode.Playback, OnAudioFocusLost);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    // ChatAudioUI is disabled until the moment user visits ChatPage
    public void Enable()
        => _whenEnabledSource.TrySetResult();

    public void ShowAudioDiagnostics(ChatId chatId)
        => _ = ModalUI.Show(new AudioDiagnosticsModal.Model(chatId));

    [ComputeMethod] // Synced
    public virtual Task<ChatAudioState> GetState(ChatId chatId)
    {
        var activeChats = ActiveChatsUI.ActiveChats.Value;
        var isListening = false;
        var isRecording = false;
        if (activeChats.TryGetValue(chatId, out var activeChat)) {
            isListening = activeChat.IsListening;
            isRecording = activeChat.IsRecording;
        }
        var isReplaying = _replayState.Value is { } hps && hps.ChatId == chatId;
        var result = new ChatAudioState(chatId, isListening, isReplaying, isRecording);
        return Task.FromResult(result);
    }

    [ComputeMethod] // Synced
    public virtual async Task<Moment?> GetStopListeningAt(ChatId chatId, CancellationToken cancellationToken)
    {
        var map = await _stopListeningAtMap.Use(cancellationToken).ConfigureAwait(false);
        return map.TryGetValue(chatId, out var stopAt) ? stopAt : null;
    }

    [ComputeMethod(MinCacheDuration = 300)] // Synced
    public virtual async Task<List<ChatId>> GetChatsYouNeedToKeepListeningTo(CancellationToken cancellationToken)
    {
        await Hub.ChatUI.WhenReady.ConfigureAwait(false);
        var alwaysListened = await UserSettingsUI.UserListeningSettings()
            .Get(x => x.AlwaysListenedChatIds, cancellationToken)
            .ConfigureAwait(false);
        // A PTT chat wakes you to hear someone, so it must also be listened to -
        // arming alone starts no player.
        var pttChatIds = await GetPttChatIds(cancellationToken).ConfigureAwait(false);
        return alwaysListened.Concat(pttChatIds).Distinct().ToList();
    }

    [ComputeMethod(MinCacheDuration = 300)] // Synced
    public virtual async Task<List<ChatId>> GetPttChatIds(CancellationToken cancellationToken)
    {
        // Armed = consent within the chat's current enable-epoch; the Chats.Get dependency
        // re-arms/disarms everything downstream when an owner flips the chat's PTT toggle.
        await Hub.ChatUI.WhenReady.ConfigureAwait(false);
        var pttChats = await UserSettingsUI.UserWalkieTalkieSettings()
            .Get(x => x.PttChats, cancellationToken)
            .ConfigureAwait(false);
        var result = new List<ChatId>(pttChats.Length);
        foreach (var pttChat in pttChats) {
            var chat = await Chats.Get(Session, pttChat.ChatId, cancellationToken).ConfigureAwait(false);
            if (chat != null && UserWalkieTalkieSettings.IsArmed(chat.PttEnabledAt, pttChat.JoinedAt))
                result.Add(pttChat.ChatId);
        }
        return result;
    }

    [ComputeMethod] // Synced
    public virtual Task<ImmutableHashSet<ChatId>> GetListeningChatIds()
        => Task.FromResult(ActiveChatsUI.ActiveChats.Value.Where(c => c.IsListening).Select(c => c.ChatId).ToImmutableHashSet());

    public ValueTask SetListeningState(ChatId chatId, bool mustListen)
    {
        if (mustListen)
            Hub.AudioAttachmentPlayer.OnConversationJoined();
        var now = CpuNow;
        return ActiveChatsUI.UpdateActiveChats(activeChats => {
            if (activeChats.TryGetValue(chatId, out var chat)) {
                if (chat.IsListening == mustListen)
                    return activeChats;

                chat = chat with {
                    IsListening = mustListen,
                    Recency = mustListen ? now : chat.Recency,
                    ListeningRecency = mustListen ? now : chat.ListeningRecency,
                };
                activeChats = activeChats.WithOrReplace(chat).ToArray();
            }
            else if (mustListen)
                activeChats = activeChats.With(new ActiveChat(chatId, true, false, now, now), true);
            return activeChats;
        });
    }

    public ValueTask ClearListeningChats()
        => ActiveChatsUI.UpdateActiveChats(activeChats => {
            var newActiveChats = new List<ActiveChat>(activeChats.Length);
            var isUpdated = false;
            foreach (var chat in activeChats) {
                if (chat.IsListening) {
                    newActiveChats.Add(chat with { IsListening = false });
                    isUpdated = true;
                }
                else
                    newActiveChats.Add(chat);
            }
            return isUpdated ? newActiveChats.ToArray() : activeChats;
        });

    [ComputeMethod] // Synced
    public virtual Task<ChatId?> GetRecordingChatId()
    {
        var activeChats = ActiveChatsUI.ActiveChats.Value;
        var recordingChat = activeChats.FirstOrDefault(c => c.IsRecording);
        return Task.FromResult(recordingChat?.ChatId);
    }

    public bool IsRecording()
        => ActiveChatsUI.ActiveChats.Value.Any(c => c.IsRecording);

    public static RecordingIdleOptions GetRecordingIdleOptions(TimeSpan? idleDuration, AudioSettings audioSettings)
    {
        if (idleDuration is not { } duration)
            return new RecordingIdleOptions(
                Constants.Audio.RecordingDuration,
                audioSettings.IdleRecordingPreCountdownTimeout,
                audioSettings.IdleRecordingCheckPeriod);

        var preCountdown = Constants.Audio.RecordingDuration - audioSettings.IdleRecordingPreCountdownTimeout;
        return new RecordingIdleOptions(
            duration,
            (duration - preCountdown).Positive(),
            audioSettings.IdleRecordingCheckPeriod);
    }

    public ValueTask SetRecordingChatId(
        ChatId? chatId,
        bool isPushToTalk = false,
        TimeSpan? idleDuration = null,
        bool mustPlayBeginTune = true)
    {
        _recordingIdleDurationBox = chatId is null ? null : (object?)idleDuration;
        // Publication: RecordChat reads this from its own flow right before opening the mic.
        Volatile.Write(ref _isBeginTuneSuppressed, chatId is not null && !mustPlayBeginTune);
        if (chatId is not null)
            Hub.AudioAttachmentPlayer.OnConversationJoined();
        return ActiveChatsUI.UpdateActiveChats(activeChats => {
                var oldRecordingChat = activeChats.FirstOrDefault(c => c.IsRecording);
                if (oldRecordingChat?.ChatId == chatId)
                    return activeChats;

                var now = CpuNow;
                if (chatId is null) {
                    // End recording
                    if (oldRecordingChat != null) {
                        activeChats = activeChats.WithOrReplace(oldRecordingChat with {
                            IsRecording = false,
                            Recency = now,
                        }).ToArray();
                        _ = RestoreListening(StopToken);
                    }
                    return activeChats;
                }

                // Begin recording
                var chat = activeChats.FirstOrDefault(c => c.ChatId == chatId);
                var mustListen = !isPushToTalk;
                if (chat == null)
                    chat = new ActiveChat(chatId, mustListen, true, now, mustListen ? now : default);
                else {
                    var isListening = mustListen || chat.IsListening;
                    chat = chat with {
                        IsListening = isListening,
                        IsRecording = true,
                        Recency = now,
                        ListeningRecency = isListening && !chat.IsListening ? now : chat.ListeningRecency,
                    };
                }
                activeChats = activeChats.WithOrReplace(chat, true).ToArray();
                // Turn off listening for all the rest chats if mustListen
                activeChats = mustListen
                    ? activeChats.WithUpdate(
                        c => c.ChatId != chatId && (c.IsRecording || c.IsListening),
                        c => c with { IsRecording = false, IsListening = false })
                        .ToArray()
                    : activeChats.WithUpdate(
                        c => c.ChatId != chatId && c.IsRecording,
                        c => c with { IsRecording = false })
                        .ToArray();
                return activeChats;

                async Task RestoreListening(CancellationToken ct)
                {
                    var chatIdsToListenTo = await GetChatsYouNeedToKeepListeningTo(ct).ConfigureAwait(false);
                    foreach (var cid in chatIdsToListenTo) {
                        chat = activeChats.FirstOrDefault(c => c.ChatId == cid);
                        chat = chat == null
                            ? new ActiveChat(cid,
                                true,
                                false,
                                now,
                                now)
                            : chat with {
                                IsListening = true,
                                ListeningRecency = now,
                            };
                        activeChats = activeChats.WithOrReplace(chat).ToArray();
                    }
                }
            },
            StopToken);
    }
}
