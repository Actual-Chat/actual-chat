using ActualChat.Kvas;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ActiveChatsUI
{
    public const int MaxActiveChatCount = 3;
    public static readonly TimeSpan MaxContinueListeningRecency = TimeSpan.FromMinutes(5);

    private readonly AsyncLock _updateLock = new(LockReentryMode.CheckedFail);
    private readonly IStoredState<ApiArray<ActiveChat>> _activeChats;
    private ILogger? _log;

    private ChatHub ChatHub { get; }
    private Session Session => ChatHub.Session;
    private IChats Chats => ChatHub.Chats;
    private IStateFactory StateFactory => ChatHub.StateFactory();
    private LocalSettings LocalSettings => ChatHub.LocalSettings();
    private UICommander UICommander => ChatHub.UICommander();
    private MomentClockSet Clocks => ChatHub.Clocks();
    private ILogger Log => _log ??= ChatHub.LogFor(GetType());
    private ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

    private Moment Now => Clocks.SystemClock.Now;

    public IMutableState<ApiArray<ActiveChat>> ActiveChats => _activeChats;
    public Task WhenLoaded => _activeChats.WhenRead;

    public ActiveChatsUI(ChatHub chatHub)
    {
        ChatHub = chatHub;
        _activeChats = StateFactory.NewKvasStored<ApiArray<ActiveChat>>(
            new (LocalSettings, nameof(ActiveChats)) {
                Corrector = FixStoredActiveChats,
                Category = StateCategories.Get(GetType(), nameof(ActiveChats)),
            });
    }

    public async ValueTask UpdateActiveChats(
        Func<ApiArray<ActiveChat>, ApiArray<ActiveChat>> updater,
        CancellationToken cancellationToken = default)
    {
        using (var releaser = await _updateLock.Lock(cancellationToken).ConfigureAwait(false)) {
            releaser.MarkLockedLocally();

            var originalValue = ActiveChats.Value;
            var updatedValue = updater.Invoke(originalValue);
            if (originalValue == updatedValue)
                return;

            updatedValue = await FixActiveChats(updatedValue, cancellationToken).ConfigureAwait(false);
            ActiveChats.Value = updatedValue;
        }
        _ = UICommander.RunNothing();
    }

    public ValueTask RemoveActiveChat(ChatId chatId)
        => chatId.IsNone ? default
            : UpdateActiveChats(c => c.RemoveAll(chatId));

    private ValueTask<ApiArray<ActiveChat>> FixStoredActiveChats(
        ApiArray<ActiveChat> activeChats,
        CancellationToken cancellationToken = default)
    {
        // Turn off stored recording on restoring state during app start
        activeChats = activeChats
            .Select(chat => {
                if (chat.IsRecording)
                    chat = chat with { IsRecording = false };

                var listeningRecency = Moment.Max(chat.Recency, chat.ListeningRecency);
                if (chat.IsListening && Now - listeningRecency > MaxContinueListeningRecency)
                    chat = chat with { IsListening = false };

                return chat;
            })
            .ToApiArray();
        return FixActiveChats(activeChats, cancellationToken);
    }

    private async ValueTask<ApiArray<ActiveChat>> FixActiveChats(
        ApiArray<ActiveChat> activeChats,
        CancellationToken cancellationToken = default)
    {
        if (activeChats.Count == 0)
            return activeChats;

        // Removing chats that violate access rules + enforce "just 1 recording chat" rule
        var chatsAndRules = await activeChats
            .Select(async chat => (
                Chat: chat,
                Rules: await Chats.GetRules(Session, chat.ChatId, cancellationToken).ConfigureAwait(false)))
            .Collect()
            .ConfigureAwait(false);

        var recordingChat = chatsAndRules
            .Where(x => x.Chat.IsRecording && x.Rules.CanWrite())
            .OrderByDescending(x => x.Chat.Recency)
            .FirstOrDefault()
            .Chat;
        foreach (var (chat, rules) in chatsAndRules) {
            // There must be just 1 recording chat
            var newChat = chat;
            if (newChat.IsRecording && newChat.ChatId != recordingChat.ChatId)
                newChat = newChat with { IsRecording = false };
            if (!(newChat.IsListening || newChat.IsRecording)) // Must be active
                newChat = default;
            else if (!rules.CanRead()) // Must be accessible
                newChat = default;

            if (!chat.IsSameAs(newChat))
                activeChats = newChat.IsNone
                    ? activeChats.RemoveAll(chat)
                    : activeChats.AddOrReplace(newChat);
        }

        // There must be no more than MaxActiveChatCount active chats
        while (activeChats.Count > MaxActiveChatCount) {
            var chat = activeChats[^1];
            if (chat.IsRecording)
                chat = activeChats[^2];
            activeChats = activeChats.RemoveAll(chat);
        }
        return activeChats;
    }
}
