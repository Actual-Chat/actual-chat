using ActualChat.Audio;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatAudioUI : WorkerBase
{
    private readonly IMutableState<Moment?> _stopRecordingAt;
    private readonly TaskSource<Unit> _whenEnabledSource;

    private Session Session { get; }
    private AudioSettings AudioSettings { get; }
    private AudioRecorder AudioRecorder { get; }
    private ChatPlayers ChatPlayers { get; }
    private IChats Chats { get; }
    private Func<ChatUI> ChatUIAccessor { get; }
    private ActiveChatsUI ActiveChatsUI { get; }
    private TuneUI TuneUI { get; }
    private LanguageUI LanguageUI { get; }
    private InteractiveUI InteractiveUI { get; }
    private DeviceAwakeUI DeviceAwakeUI { get; }
    private UICommander UICommander { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    private Moment Now => Clocks.SystemClock.Now;
    public IState<Moment?> StopRecordingAt => _stopRecordingAt;
    public Task<Unit> WhenEnabled => _whenEnabledSource.Task;

    public ChatAudioUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        AudioSettings = services.GetRequiredService<AudioSettings>();
        AudioRecorder = services.GetRequiredService<AudioRecorder>();
        ChatPlayers = services.GetRequiredService<ChatPlayers>();
        Clocks = services.Clocks();
        Chats = services.GetRequiredService<IChats>();
        ChatUIAccessor = services.GetRequiredService<ChatUI>;
        LanguageUI = services.GetRequiredService<LanguageUI>();
        InteractiveUI = services.GetRequiredService<InteractiveUI>();
        TuneUI = services.GetRequiredService<TuneUI>();
        ActiveChatsUI = services.GetRequiredService<ActiveChatsUI>();
        DeviceAwakeUI = services.GetRequiredService<DeviceAwakeUI>();
        UICommander = services.UICommander();
        Log = services.LogFor(GetType());

        _whenEnabledSource = TaskSource.New<Unit>(true);
        // Read entry states from other windows / devices are delayed by 1s
        _stopRecordingAt = services.StateFactory().NewMutable<Moment?>();
        Start();
    }

    // ChatAudioUI is disabled until the moment user visits ChatPage
    public void Enable()
        => _whenEnabledSource.TrySetResult(default);

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
            return ValueTask.CompletedTask;

        var now = Now;
        return ActiveChatsUI.UpdateActiveChats(activeChats => {
            var oldActiveChats = activeChats;
            if (activeChats.TryGetValue(chatId, out var chat) && chat.IsListening != mustListen) {
                chat = chat with {
                    IsListening = mustListen,
                    ListeningRecency = mustListen ? now : chat.ListeningRecency,
                };
                activeChats = activeChats.AddOrUpdate(chat);
            }
            else if (mustListen)
                activeChats = activeChats.Add(new ActiveChat(chatId, true, false, now, now));
            if (oldActiveChats != activeChats)
                UICommander.RunNothing();

            return activeChats;
        });
    }

    public ValueTask ClearListeningState()
        => ActiveChatsUI.UpdateActiveChats(activeChats => {
            var oldActiveChats = activeChats;
            foreach (var chat in oldActiveChats) {
                if (chat.IsListening)
                    activeChats = activeChats.AddOrUpdate(chat with { IsListening = false });
            }
            if (oldActiveChats != activeChats)
                UICommander.RunNothing();

            return activeChats;
        });

    [ComputeMethod] // Synced
    public virtual Task<ChatId> GetRecordingChatId()
        => Task.FromResult(ActiveChatsUI.ActiveChats.Value.FirstOrDefault(c => c.IsRecording).ChatId);

    public ValueTask SetRecordingChatId(ChatId chatId)
        => ActiveChatsUI.UpdateActiveChats(activeChats => {
            var oldChat = activeChats.FirstOrDefault(c => c.IsRecording);
            if (oldChat.ChatId == chatId)
                return activeChats;

            if (!oldChat.ChatId.IsNone)
                activeChats = activeChats.AddOrUpdate(oldChat with {
                    IsRecording = false,
                    Recency = Now,
                });
            if (!chatId.IsNone) {
                var newChat = new ActiveChat(chatId, true, true, Now);
                activeChats = activeChats.AddOrUpdate(newChat);
                TuneUI.Play("begin-recording");
            }
            else
                TuneUI.Play("end-recording");

            UICommander.RunNothing();
            return activeChats;
        });

    [ComputeMethod]
    public virtual async Task<RealtimePlaybackState?> GetExpectedRealtimePlaybackState()
    {
        var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
        return listeningChatIds.Count == 0 ? null : new RealtimePlaybackState(listeningChatIds);
    }
}
