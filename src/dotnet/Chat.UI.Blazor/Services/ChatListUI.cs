using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatListUI : WorkerBase
{
    public const int MaxActiveChatCount = 3;
    private const int MinRestartDelayMs = 100;
    private const int MaxRestartDelayMs = 2_000;
    private readonly IMutableState<ChatId> _selectedChatId;
    private bool _isActiveChatsFirstLoad = true;
    private readonly object _lock = new();
    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);
    public IStoredState<ImmutableHashSet<ActiveChat>> ActiveChats { get; }
    public IState<ChatId> SelectedChatId => _selectedChatId;

    private Session Session { get; }
    private IChats Chats { get; }
    private TuneUI TuneUI { get; }
    private UICommander UICommander { get; }
    private UIEventHub UIEventHub { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;
    private ILogger Log { get; }

    public ChatListUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Chats = services.GetRequiredService<IChats>();
        TuneUI = services.GetRequiredService<TuneUI>();
        UICommander = services.UICommander();
        UIEventHub = services.UIEventHub();
        Clocks = services.Clocks();
        Log = services.LogFor<ChatListUI>();

        _selectedChatId = services.StateFactory().NewMutable<ChatId>();
        ActiveChats = services.StateFactory().NewKvasStored<ImmutableHashSet<ActiveChat>>(
            new (services.LocalSettings(), nameof(ActiveChats)) {
                InitialValue = ImmutableHashSet<ActiveChat>.Empty,
                Corrector = FixStoredActiveChats,
            });
    }

    [ComputeMethod] // Synced
    public virtual Task<bool> IsSelected(ChatId chatId)
        => Task.FromResult(!chatId.IsNone && SelectedChatId.Value == chatId);

    public void SelectChat(ChatId chatId)
    {
        lock (_lock) {
            if (_selectedChatId.Value == chatId)
                return;

            _selectedChatId.Value = chatId;
        }
        _ = TuneUI.Play("select-chat");
        _ = UIEventHub.Publish<SelectedChatChangedEvent>(CancellationToken.None);
        UICommander.RunNothing();
    }

    protected override Task RunInternal(CancellationToken cancellationToken)
        => Task.WhenAll(
            InvalidateSelectedChatDependencies(cancellationToken),
            Task.CompletedTask);

    private ValueTask<ImmutableHashSet<ActiveChat>> FixStoredActiveChats(
        ImmutableHashSet<ActiveChat> activeChats,
        CancellationToken cancellationToken = default)
    {
        if (_isActiveChatsFirstLoad) {
            // Turn off stored recording on restoring state during app start
            _isActiveChatsFirstLoad = false;
            if (activeChats.Count > 0) {
                activeChats = activeChats
                    .Select(c => c.IsRecording ? c with {IsRecording = false} : c)
                    .ToImmutableHashSet();
            }
        }
        return FixActiveChats(activeChats, cancellationToken);
    }

    private async Task InvalidateSelectedChatDependencies(CancellationToken cancellationToken)
    {
        while (true) {
            try {
                var oldChatId = SelectedChatId.Value;
                var changes = SelectedChatId.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
                await foreach (var cSelectedContactId in changes.ConfigureAwait(false)) {
                    var newChatId = cSelectedContactId.Value;
                    if (newChatId == oldChatId)
                        continue;

                    Log.LogDebug("InvalidateSelectedChatDependencies: *");
                    using (Computed.Invalidate()) {
                        _ = IsSelected(oldChatId);
                        _ = IsSelected(newChatId);
                    }

                    oldChatId = newChatId;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, $"{nameof(InvalidateSelectedChatDependencies)} failed");
            }
            var random = Random.Shared.Next(MinRestartDelayMs, MaxRestartDelayMs);
            await Clocks.CoarseCpuClock.Delay(random, cancellationToken);
        }
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
