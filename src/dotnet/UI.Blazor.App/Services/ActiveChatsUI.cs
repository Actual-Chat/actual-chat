using ActualChat.Kvas;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.App.Services;

public class ActiveChatsUI : UIServiceBase<AppUIHub>
{
    public const int MaxActiveChatCount = 3;

    private readonly AsyncLock _updateLock = new(LockReentryMode.CheckedFail);
    private readonly StoredState<ActiveChat[]> _activeChats;

    private IChats Chats => Hub.Chats;
    private Moment CpuNow => Clocks.CpuClock.Now;

    public MutableState<ActiveChat[]> ActiveChats => _activeChats;
    public Task WhenReady => _activeChats.WhenRead;

    public ActiveChatsUI(AppUIHub hub) : base(hub)
        => _activeChats = StateFactory.NewKvasStored<ActiveChat[]>(
            new (LocalSettings, nameof(ActiveChats)) {
                InitialValue = [],
                Corrector = FixStoredActiveChats,
                Category = StateCategories.Get(GetType(), nameof(ActiveChats)),
            });

    public async ValueTask UpdateActiveChats(
        Func<ActiveChat[], ActiveChat[]> updater,
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
        => UpdateActiveChats(c => c.Without(chatId).ToArray());

    private async ValueTask<ActiveChat[]> FixStoredActiveChats(
        ActiveChat[] activeChats,
        CancellationToken cancellationToken = default)
    {
        // Turn off stored recording on restoring state during app start
        activeChats = activeChats
            .Select(chat => {
                if (chat.IsRecording)
                    chat = chat with { IsRecording = false };

                var listeningRecency = Moment.Max(chat.Recency, chat.ListeningRecency);
                if (chat.IsListening && CpuNow - listeningRecency > Constants.Audio.ListeningDuration)
                    chat = chat with { IsListening = false };

                return chat;
            })
            .ToArray();
        return await FixActiveChats(activeChats, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ActiveChat[]> FixActiveChats(
        ActiveChat[] activeChats,
        CancellationToken cancellationToken = default)
    {
        if (activeChats.Length == 0)
            return activeChats;

        // Removing chats that violate access rules + enforce "just 1 recording chat" rule
        var chatsAndRules = await activeChats
            .Select(async chat => (
                Chat: chat,
                Rules: await Chats.GetRules(Session, chat.ChatId, cancellationToken).ConfigureAwait(false)))
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);

        var recordingChatId = chatsAndRules
            .Where(x => x.Chat.IsRecording && x.Rules.CanWrite())
            .OrderByDescending(x => x.Chat.Recency)
            .FirstOrDefault()
            .Chat?.ChatId;
        foreach (var (chat, rules) in chatsAndRules) {
            // There must be just 1 recording chat, and none when write access is gone
            var newChat = chat;
            if (newChat.IsRecording && newChat.ChatId != recordingChatId)
                newChat = newChat with { IsRecording = false };
            if (!(newChat.IsListening || newChat.IsRecording)) // Must be active
                newChat = null;
            else if (!rules.CanRead()) // Must be accessible
                newChat = null;

            if (!chat.IsSameAs(newChat))
                activeChats = newChat == null
                    ? activeChats.Without(chat).ToArray()
                    : activeChats.WithOrReplace(newChat).ToArray();
        }

        // There must be no more than MaxActiveChatCount active chats
        while (activeChats.Length > MaxActiveChatCount) {
            var chat = activeChats[^1];
            if (chat.IsRecording)
                chat = activeChats[^2];
            activeChats = activeChats.Without(chat).ToArray();
        }
        return activeChats;
    }
}
