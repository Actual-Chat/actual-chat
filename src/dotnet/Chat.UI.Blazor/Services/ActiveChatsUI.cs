using ActualChat.Kvas;
using ActualChat.Users;
using ActualLab.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ActiveChatsUI : ScopedServiceBase<ChatUIHub>
{
    public const int MaxActiveChatCount = 3;
    public static readonly TimeSpan MaxContinueListeningRecency = TimeSpan.FromMinutes(5);

    private readonly AsyncLock _updateLock = new(LockReentryMode.CheckedFail);
    private readonly IStoredState<ApiArray<ActiveChat>> _activeChats;

    private IChats Chats => Hub.Chats;
    private UICommander UICommander => Hub.UICommander();
    private Moment CpuNow => Clocks.CpuClock.Now;

    public IMutableState<ApiArray<ActiveChat>> ActiveChats => _activeChats;
    public Task WhenLoaded => _activeChats.WhenRead;

    public ActiveChatsUI(ChatUIHub hub) : base(hub)
        => _activeChats = StateFactory.NewKvasStored<ApiArray<ActiveChat>>(
            new (LocalSettings, nameof(ActiveChats)) {
                Corrector = FixStoredActiveChats,
                Category = StateCategories.Get(GetType(), nameof(ActiveChats)),
            });

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

    private async ValueTask<ApiArray<ActiveChat>> FixStoredActiveChats(
        ApiArray<ActiveChat> activeChats,
        CancellationToken cancellationToken = default)
    {
        // Turn off stored recording on restoring state during app start
        activeChats = await activeChats
            .Select(async chat => {
                if (chat.IsRecording)
                    chat = chat with { IsRecording = false };

                var userChatSettings = await AccountSettings
                    .GetUserChatSettings(chat.ChatId, cancellationToken)
                    .ConfigureAwait(false);
                var listeningMode = userChatSettings.ListeningMode;
                var continueListeningRecency = listeningMode switch {
                    ListeningMode.Default => MaxContinueListeningRecency,
                    ListeningMode.TurnOffAfter15Minutes => TimeSpan.FromMinutes(15),
                    ListeningMode.TurnOffAfter2Hours => TimeSpan.FromHours(2),
                    ListeningMode.KeepListening => TimeSpan.MaxValue,
 #pragma warning disable CA2208
                    _ => throw new ArgumentOutOfRangeException(nameof(listeningMode)),
 #pragma warning restore CA2208
                };
                var listeningRecency = Moment.Max(chat.Recency, chat.ListeningRecency);
                if (chat.IsListening && CpuNow - listeningRecency > continueListeningRecency)
                    chat = chat with { IsListening = false };
                else if (listeningMode == ListeningMode.KeepListening)
                    chat = chat with { IsListening = true };

                return chat;
            })
            .Collect()
            .ToApiArray()
            .ConfigureAwait(false);
        return await FixActiveChats(activeChats, cancellationToken).ConfigureAwait(false);
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
