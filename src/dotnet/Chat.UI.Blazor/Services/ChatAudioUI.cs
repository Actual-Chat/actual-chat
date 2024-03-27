using ActualChat.Audio;
using ActualChat.Streaming.UI.Blazor.Components;
using ActualChat.Streaming.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatAudioUI : ScopedWorkerBase<ChatUIHub>, IComputeService, INotifyInitialized
{
    private readonly IMutableState<Moment?> _stopRecordingAt;
    private readonly IMutableState<Moment?> _audioStoppedAt;
    private readonly IMutableState<NextBeepState?> _nextBeep;
    private readonly TaskCompletionSource _whenEnabledSource = TaskCompletionSourceExt.New();

    private IChats Chats => Hub.Chats;
    private ChatActivity ChatActivity => Hub.ChatActivity;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;
    private AudioInitializer AudioInitializer => Hub.AudioInitializer;
    private AudioSettings AudioSettings => Hub.AudioSettings;
    private AudioRecorder AudioRecorder => Hub.AudioRecorder;
    private ChatPlayers ChatPlayers => Hub.ChatPlayers;
    private ChatEditorUI ChatEditorUI => Hub.ChatEditorUI;
    private LanguageUI LanguageUI => Hub.LanguageUI;
    private ModalUI ModalUI => Hub.ModalUI;
    private UserActivityUI UserActivityUI => Hub.UserActivityUI;
    private InteractiveUI InteractiveUI => Hub.InteractiveUI;
    private DeviceAwakeUI DeviceAwakeUI => Hub.DeviceAwakeUI;
    private TuneUI TuneUI => Hub.TuneUI;
    private Dispatcher Dispatcher => Hub.Dispatcher;

    private Moment CpuNow => Clocks.CpuClock.Now;
    private Moment ServerNow => Clocks.ServerClock.Now;

    public IState<Moment?> StopRecordingAt => _stopRecordingAt; // CPU time
    public IState<Moment?> AudioStoppedAt => _audioStoppedAt; // CPU time
    public IState<NextBeepState?> NextBeep => _nextBeep;
    public Task WhenEnabled => _whenEnabledSource.Task;

    public ChatAudioUI(ChatUIHub hub) : base(hub)
    {
        // Read entry states from other windows / devices are delayed by 1s
        var type = GetType();
        var stateFactory = StateFactory;
        _stopRecordingAt = stateFactory.NewMutable((Moment?)null, StateCategories.Get(type, nameof(StopRecordingAt)));
        _audioStoppedAt = stateFactory.NewMutable((Moment?)null, StateCategories.Get(type, nameof(AudioStoppedAt)));
        _nextBeep = stateFactory.NewMutable((NextBeepState?)null, StateCategories.Get(type, nameof(NextBeep)));
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
                activeChats = activeChats.AddOrReplace(chat);
            }
            else if (mustListen)
                activeChats = activeChats.Add(new ActiveChat(chatId, true, false, now, now), true);
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

                var now = CpuNow;
                if (chatId.IsNone) {
                    // End recording
                    if (!oldRecordingChat.IsNone) {
                        activeChats = activeChats.AddOrReplace(oldRecordingChat with {
                            IsRecording = false,
                            Recency = now,
                        });
                        _ = TuneUI.Play(Tune.EndRecording);
                    }
                    return activeChats;
                }

                // Begin recording
                var chat = activeChats.FirstOrDefault(c => c.ChatId == chatId);
                var mustListen = !isPushToTalk;
                if (chat.IsNone)
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
                activeChats = activeChats.AddOrReplace(chat, true);
                activeChats = mustListen
                    ? activeChats.UpdateWhere(
                        c => c.ChatId != chatId && (c.IsRecording || c.IsListening),
                        c => c with { IsRecording = false, IsListening = false })
                    : activeChats.UpdateWhere(
                        c => c.ChatId != chatId && c.IsRecording,
                        c => c with { IsRecording = false });
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
