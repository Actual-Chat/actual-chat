using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class ChatPinsUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    public Task Pin(ChatEntryId entryId) => SetPinned(entryId, true);
    public Task Unpin(ChatEntryId entryId) => SetPinned(entryId, false);

    // The server is the single source of truth: it re-normalizes the list (drops removed ids) and
    // enforces the cap atomically per command, so concurrent moderators can't lose each other's pins.
    private async Task SetPinned(ChatEntryId entryId, bool mustPin)
    {
        var result = await UICommander.Run(new Chats_SetPinned {
            Session = Session,
            EntryId = entryId,
            MustPin = mustPin,
        }).ConfigureAwait(true);
        if (result.HasError)
            return; // The command pipeline surfaces the error (e.g. the cap message).
        ToastUI.Show(mustPin ? L.Pin_MessagePinned : L.Pin_MessageUnpinned, ToastDismissDelay.Short);
    }
}
