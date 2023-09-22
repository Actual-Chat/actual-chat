using ActualChat.Audio;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Stl.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatAudioUI : WorkerBase, IComputeService, INotifyInitialized
{
    private readonly IMutableState<Moment?> _stopRecordingAt;
    private readonly IMutableState<Moment?> _audioStoppedAt;
    private readonly IMutableState<NextBeepState?> _nextBeep;
    private readonly TaskCompletionSource _whenEnabledSource = TaskCompletionSourceExt.New();
    private AudioSettings? _audioSettings;
    private AudioRecorder? _audioRecorder;
    private ChatPlayers? _chatPlayers;
    private IChats? _chats;
    private ActiveChatsUI? _activeChatsUI;
    private TuneUI? _tuneUI;
    private LanguageUI? _languageUI;
    private InteractiveUI? _interactiveUI;
    private DeviceAwakeUI? _deviceAwakeUI;
    private ChatEditorUI? _chatEditorUI;
    private UserActivityUI? _userActivityUI;

    private IServiceProvider Services { get; }
    private ILogger Log { get; }
    private ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

    private Session Session { get; }
    private AudioSettings AudioSettings => _audioSettings ??= Services.GetRequiredService<AudioSettings>();
    private AudioRecorder AudioRecorder => _audioRecorder ??= Services.GetRequiredService<AudioRecorder>();
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    private ActiveChatsUI ActiveChatsUI => _activeChatsUI ??= Services.GetRequiredService<ActiveChatsUI>();
    private TuneUI TuneUI => _tuneUI ??= Services.GetRequiredService<TuneUI>();
    private LanguageUI LanguageUI => _languageUI ??= Services.GetRequiredService<LanguageUI>();
    private InteractiveUI InteractiveUI => _interactiveUI ??= Services.GetRequiredService<InteractiveUI>();
    private DeviceAwakeUI DeviceAwakeUI => _deviceAwakeUI ??= Services.GetRequiredService<DeviceAwakeUI>();
    private ChatEditorUI ChatEditorUI => _chatEditorUI ??= Services.GetRequiredService<ChatEditorUI>();
    private UserActivityUI UserActivityUI => _userActivityUI ??= Services.GetRequiredService<UserActivityUI>();
    private MomentClockSet Clocks { get; }

    private Moment Now => Clocks.SystemClock.Now;

    public IState<Moment?> StopRecordingAt => _stopRecordingAt;
    public Task WhenEnabled => _whenEnabledSource.Task;
    public IState<Moment?> AudioStoppedAt => _audioStoppedAt;
    public IState<NextBeepState?> NextBeep => _nextBeep;

    public ChatAudioUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        Session = services.Session();
        Clocks = services.Clocks();

        // Read entry states from other windows / devices are delayed by 1s
        var stateFactory = services.StateFactory();
        _stopRecordingAt = stateFactory.NewMutable<Moment?>();
        _audioStoppedAt = stateFactory.NewMutable<Moment?>();
        _nextBeep = stateFactory.NewMutable<NextBeepState?>();
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    // ChatAudioUI is disabled until the moment user visits ChatPage
    public void Enable()
        => _whenEnabledSource.TrySetResult();

    [ComputeMethod] // Synced
    public virtual Task<ChatAudioState> GetState(ChatId chatId)
    {
        if (chatId.IsNone)
            return Task.FromResult(ChatAudioState.None);

        var activeChats = ActiveChatsUI.ActiveChats.Value;
        activeChats.TryGetValue(chatId, out var activeChat);
        var isListening = activeChat.IsListening;
        var isRecording = activeChat.IsRecording;
        var isPlayingHistorical = ChatPlayers.PlaybackState.Value is HistoricalPlaybackState hps && hps.ChatId == chatId;
        var result = new ChatAudioState(chatId, isListening, isPlayingHistorical, isRecording);
        return Task.FromResult(result);
    }

    [ComputeMethod] // Synced
    public virtual Task<ImmutableHashSet<ChatId>> GetListeningChatIds()
        => Task.FromResult(ActiveChatsUI.ActiveChats.Value.Where(c => c.IsListening).Select(c => c.ChatId).ToImmutableHashSet());

    public ValueTask SetListeningState(ChatId chatId, bool mustListen)
    {
        if (chatId.IsNone)
            return default;

        var now = Now;
        return ActiveChatsUI.UpdateActiveChats(activeChats => {
            if (activeChats.TryGetValue(chatId, out var chat) && chat.IsListening != mustListen) {
                chat = chat with {
                    IsListening = mustListen,
                    ListeningRecency = mustListen ? now : chat.ListeningRecency,
                };
                activeChats = activeChats.AddOrReplace(chat);
            }
            else if (mustListen)
                activeChats = activeChats.Add(new ActiveChat(chatId, true, false, now, now));
            return activeChats;
        });
    }

    public ValueTask ClearListeningChats()
        => ActiveChatsUI.UpdateActiveChats(activeChats => {
            var newActiveChats = new List<ActiveChat>(activeChats.Count);
            var isUpdated = false;
            foreach (var chat in activeChats) {
                if (chat.IsListening) {
                    newActiveChats.Add(chat with { IsListening = false });
                    isUpdated = true;
                }
                else
                    newActiveChats.Add(chat);
            }
            return isUpdated ? new ApiArray<ActiveChat>(newActiveChats) : activeChats;
        });

    [ComputeMethod] // Synced
    public virtual Task<ChatId> GetRecordingChatId()
        => Task.FromResult(ActiveChatsUI.ActiveChats.Value.FirstOrDefault(c => c.IsRecording).ChatId);

    public ValueTask SetRecordingChatId(ChatId chatId, bool isPushToTalk = false)
        => ActiveChatsUI.UpdateActiveChats(activeChats => {
                var oldRecordingChat = activeChats.FirstOrDefault(c => c.IsRecording);
                if (oldRecordingChat.ChatId == chatId)
                    return activeChats;

                if (chatId.IsNone) {
                    // End recording
                    if (!oldRecordingChat.IsNone) {
                        activeChats = activeChats.AddOrReplace(oldRecordingChat with {
                            IsRecording = false,
                            Recency = Now,
                        });
                        _ = TuneUI.Play(Tune.EndRecording);
                    }
                    return activeChats;
                }

                // Begin recording
                var chat = activeChats.FirstOrDefault(c => c.ChatId == chatId);
                if (chat.IsNone)
                    chat = new ActiveChat(chatId, !isPushToTalk, true, Now);
                else
                    chat = chat with {
                        IsListening = !isPushToTalk || chat.IsListening,
                        IsRecording = true,
                        Recency = Now,
                    };
                activeChats = activeChats.AddOrReplace(chat);
                activeChats = activeChats.UpdateWhere(
                    c => c.ChatId != chatId && (c.IsRecording || (!isPushToTalk && c.IsListening)),
                    c => c with { IsListening = false, IsRecording = false });
                _ = TuneUI.Play(Tune.BeginRecording);
                return activeChats;
            },
            StopToken);

    [ComputeMethod] // Synced
    public virtual Task<bool> IsAudioOn()
        => Task.FromResult(ActiveChatsUI.ActiveChats.Value.Any(c => c.IsRecording || c.IsListening));

    [ComputeMethod]
    public virtual async Task<RealtimePlaybackState?> GetExpectedRealtimePlaybackState()
    {
        var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
        return listeningChatIds.Count == 0 ? null : new RealtimePlaybackState(listeningChatIds);
    }
}
