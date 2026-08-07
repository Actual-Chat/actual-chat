using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class ChatPinsUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private IChats Chats => Hub.Chats;

    public async Task Pin(ChatEntryId entryId)
    {
        var chatId = entryId.ChatId;
        var pinned = await Chats.ListPinnedEntries(Session, chatId, default).ConfigureAwait(true);
        if (pinned.Contains(entryId))
            return;
        if (pinned.Count >= ChatPinnedEntries.MaxCount) {
            ToastUI.Show($"You can pin up to {ChatPinnedEntries.MaxCount} messages", ToastDismissDelay.Short);
            return;
        }

        // Order doesn't matter here - the server sorts pins by message chronology (Telegram-style).
        var next = pinned.Append(entryId).ToApiArray();
        await Set(chatId, next).ConfigureAwait(true);
        ToastUI.Show("Message pinned", ToastDismissDelay.Short);
    }

    public async Task Unpin(ChatEntryId entryId)
    {
        var chatId = entryId.ChatId;
        var pinned = await Chats.ListPinnedEntries(Session, chatId, default).ConfigureAwait(true);
        if (!pinned.Contains(entryId))
            return;

        var next = pinned.Where(x => x != entryId).ToApiArray();
        await Set(chatId, next).ConfigureAwait(true);
        ToastUI.Show("Message unpinned", ToastDismissDelay.Short);
    }

    public Task Set(ChatId chatId, ApiArray<ChatEntryId> entryIds)
        => UICommander.Run(new Chats_SetPinnedEntries(Session, chatId, entryIds));
}
