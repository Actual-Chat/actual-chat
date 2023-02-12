using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ActiveChatsUI
{
    public const int MaxActiveChatCount = 3;
    public static TimeSpan MaxContinueListeningRecency { get; } = TimeSpan.FromMinutes(5);

    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);

    private bool _isActiveChatsFirstLoad = true;

    private IStateFactory StateFactory { get; }
    private Session Session { get; }
    private IChats Chats { get; }
    private LocalSettings LocalSettings { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public IStoredState<ImmutableHashSet<ActiveChat>> ActiveChats { get; }

    public ActiveChatsUI(IServiceProvider services)
    {
        Clocks = services.Clocks();
        StateFactory = services.StateFactory();

        Session = services.GetRequiredService<Session>();
        Chats = services.GetRequiredService<IChats>();
        LocalSettings = services.LocalSettings();

        ActiveChats = StateFactory.NewKvasStored<ImmutableHashSet<ActiveChat>>(
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
        if (_isActiveChatsFirstLoad) {
            // Turn off stored recording on restoring state during app start
            _isActiveChatsFirstLoad = false;
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
        }
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
            .Select(async chat => {
                var rules = await Chats.GetRules(Session, chat.ChatId, default).ConfigureAwait(false);
                return (Chat: chat, Rules: rules);
            })
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
            .Select(async chat => {
                var effectiveRecency = await GetEffectiveRecency(chat, cancellationToken);
                return (Chat: chat, EffectiveRecency: effectiveRecency);
            })
            .Collect()
            .ConfigureAwait(false);
        var remainingChats = (
            from x in activeChatsWithEffectiveRecency
            orderby x.Chat.IsRecording descending, x.EffectiveRecency descending
            select x.Chat
            ).Take(MaxActiveChatCount)
            .ToImmutableHashSet();
        return remainingChats;

        async ValueTask<Moment> GetEffectiveRecency(ActiveChat chat, CancellationToken ct)
        {
            if (chat.IsRecording)
                return Clocks.CpuClock.Now;
            if (!chat.IsListening)
                return chat.Recency;

            var chatIdRange = await Chats.GetIdRange(Session, chat.ChatId, ChatEntryKind.Audio, ct);
            var chatEntryReader = Chats.NewEntryReader(Session, chat.ChatId, ChatEntryKind.Audio);
            var lastEntry = await chatEntryReader.GetLast(chatIdRange, ct);
            if (lastEntry == null)
                return chat.Recency;
            return lastEntry.IsStreaming
                ? Clocks.CpuClock.Now
                : Moment.Max(chat.Recency, lastEntry.EndsAt ?? lastEntry.BeginsAt);
        }
    }
}
