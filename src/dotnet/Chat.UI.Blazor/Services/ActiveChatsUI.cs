using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ActiveChatsUI
{
    public const int MaxActiveChatCount = 3;
    public static TimeSpan MaxContinueListeningRecency { get; } = TimeSpan.FromMinutes(5);

    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);
    private readonly IStoredState<ImmutableHashSet<ActiveChat>> _activeChats;
    private IChats? _chats;

    private IServiceProvider Services { get; }
    private ILogger Log { get; }
    private ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

    private Session Session { get; }
    private IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    private LocalSettings LocalSettings { get; }
    private IStateFactory StateFactory { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public IMutableState<ImmutableHashSet<ActiveChat>> ActiveChats => _activeChats;
    public Task WhenLoaded => _activeChats.WhenRead;

    public ActiveChatsUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        Session = services.GetRequiredService<Session>();
        LocalSettings = services.LocalSettings();
        StateFactory = services.StateFactory();
        Clocks = services.Clocks();

        _activeChats = StateFactory.NewKvasStored<ImmutableHashSet<ActiveChat>>(
            new (LocalSettings, nameof(ActiveChats)) {
                InitialValue = ImmutableHashSet<ActiveChat>.Empty,
                Corrector = FixStoredActiveChats,
                Category = StateCategories.Get(GetType(), nameof(ActiveChats)),
            });
    }

    public async ValueTask UpdateActiveChats(
        Func<ImmutableHashSet<ActiveChat>, ImmutableHashSet<ActiveChat>> updater,
        CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var originalValue = ActiveChats.Value;
        var updatedValue = updater.Invoke(originalValue);
        if (ReferenceEquals(originalValue, updatedValue))
            return;

        updatedValue = await FixActiveChats(updatedValue, cancellationToken).ConfigureAwait(false);
        ActiveChats.Value = updatedValue;
    }

    public ValueTask AddActiveChat(ChatId chatId)
    {
        if (chatId.IsNone)
            return ValueTask.CompletedTask;

        return UpdateActiveChats(activeChats => activeChats.Add(new ActiveChat(chatId, false, false, Now)));
    }

    public ValueTask RemoveActiveChat(ChatId chatId)
    {
        if (chatId.IsNone)
            return ValueTask.CompletedTask;

        return UpdateActiveChats(activeChats => activeChats.Remove(chatId));
    }

    private ValueTask<ImmutableHashSet<ActiveChat>> FixStoredActiveChats(
        ImmutableHashSet<ActiveChat> activeChats,
        CancellationToken cancellationToken = default)
    {
        if (!WhenLoaded.IsCompleted)
            return FixActiveChats(activeChats, cancellationToken);

        // Turn off stored recording on restoring state during app start
        activeChats = activeChats
            .Select(c => {
                if (c.IsRecording)
                    c = c with { IsRecording = false };
                var listeningRecency = Moment.Max(c.Recency, c.ListeningRecency);
                if (c.IsListening && Now - listeningRecency > MaxContinueListeningRecency)
                    c = c with { IsListening = false };
                return c;
            })
            .ToImmutableHashSet();
        return FixActiveChats(activeChats, cancellationToken);
    }

    private async ValueTask<ImmutableHashSet<ActiveChat>> FixActiveChats(
        ImmutableHashSet<ActiveChat> activeChats,
        CancellationToken cancellationToken = default)
    {
        if (activeChats.Count == 0)
            return activeChats;

        // Removing chats that violate access rules + enforce "just 1 recording chat" rule
        var recordingChat = activeChats.FirstOrDefault(c => c.IsRecording);
        var chatRules = await activeChats
            .Select(async chat => (
                Chat: chat,
                Rules: await Chats.GetRules(Session, chat.ChatId, cancellationToken).ConfigureAwait(false)))
            .Collect()
            .ConfigureAwait(false);
        foreach (var (c, rules) in chatRules) {
            // There must be just 1 recording chat
            var chat = c;
            if (c.IsRecording && c != recordingChat) {
                chat = chat with { IsRecording = false };
                activeChats = activeChats.AddOrUpdate(chat);
            }

            // And it must be accessible
            if (!rules.CanRead() || (chat.IsRecording && !rules.CanRead()))
                activeChats = activeChats.Remove(chat);
        }

        // There must be no more than MaxActiveChatCount active chats
        if (activeChats.Count <= MaxActiveChatCount)
            return activeChats;

        var activeChatsWithEffectiveRecency = await activeChats
            .Select(async chat => (Chat: chat, EffectiveRecency: await GetEffectiveRecency(chat, cancellationToken)))
            .Collect()
            .ConfigureAwait(false);
        var remainingChats = activeChatsWithEffectiveRecency
            .OrderByDescending(x => x.Chat.IsRecording)
            .ThenByDescending(x => x.EffectiveRecency)
            .Select(x => x.Chat)
            .Take(MaxActiveChatCount)
            .ToImmutableHashSet();
        return remainingChats;

        async ValueTask<Moment> GetEffectiveRecency(ActiveChat chat, CancellationToken cancellationToken1)
        {
            if (chat.IsRecording)
                return Clocks.CpuClock.Now;
            if (!chat.IsListening)
                return chat.Recency;

            var chatNews = await Chats.GetNews(Session, chat.ChatId, cancellationToken1).ConfigureAwait(false);
            var lastEntry = chatNews.LastTextEntry;
            if (lastEntry == null)
                return chat.Recency;
            return lastEntry.IsStreaming
                ? Clocks.CpuClock.Now
                : Moment.Max(chat.Recency, lastEntry.EndsAt ?? lastEntry.BeginsAt);
        }
    }
}
