using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class ChatPinsUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private IChats Chats => Hub.Chats;

    [ComputeMethod]
    public virtual async Task<ApiArray<ChatPin>> List(ChatId chatId, CancellationToken cancellationToken)
    {
        var sharedIds = await Chats.ListPinnedEntries(Session, chatId, cancellationToken).ConfigureAwait(false);
        var personal = await UserSettingsUI.ChatUserPinnedEntries(chatId)
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var pins = new Dictionary<ChatEntryId, ChatPin>();
        foreach (var id in sharedIds)
            pins[id] = new ChatPin(id, IsShared: true, IsPersonal: false);
        foreach (var id in personal.EntryIds)
            pins[id] = pins.TryGetValue(id, out var pin)
                ? pin with { IsPersonal = true }
                : new ChatPin(id, IsShared: false, IsPersonal: true);
        return pins.Values.OrderBy(p => p.EntryId.LocalId).ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ChatPin> Get(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        var pins = await List(entryId.ChatId, cancellationToken).ConfigureAwait(false);
        return pins.FirstOrDefault(p => p.EntryId == entryId) ?? new ChatPin(entryId, false, false);
    }

    [ComputeMethod]
    public virtual async Task<bool> CanPinForEveryone(ChatId chatId, CancellationToken cancellationToken)
    {
        // Same gate as the server's Chats_SetPinned: moderators/owners, or any peer-chat participant.
        var rules = await Chats.GetRules(Session, chatId, cancellationToken).ConfigureAwait(false);
        return rules.CanModerate() || chatId.Kind == ChatKind.Peer;
    }

    public async Task Pin(ChatEntryId entryId, bool isForEveryone)
    {
        var resultTask = isForEveryone ? SetShared(entryId, true) : SetPersonal(entryId, true);
        var result = await resultTask.ConfigureAwait(true);
        if (result.HasError)
            return;

        ToastUI.Show(isForEveryone ? L.Pin_MessagePinned : L.Pin_MessagePinnedForYou, ToastDismissDelay.Short);
    }

    public async Task Unpin(ChatEntryId entryId)
    {
        var pin = await Get(entryId, CancellationToken.None).ConfigureAwait(true);
        var canPinForEveryone = await CanPinForEveryone(entryId.ChatId, CancellationToken.None).ConfigureAwait(true);
        if (pin.IsPersonal) {
            var personalResult = await SetPersonal(entryId, false).ConfigureAwait(true);
            if (personalResult.HasError)
                return;
        }

        if (pin.IsShared && canPinForEveryone) {
            var sharedResult = await SetShared(entryId, false).ConfigureAwait(true);
            if (sharedResult.HasError)
                return;
        }

        ToastUI.Show(L.Pin_MessageUnpinned, ToastDismissDelay.Short);
    }

    // Private methods

    private Task<UIActionResult<Unit>> SetShared(ChatEntryId entryId, bool mustPin)
    {
        // The server re-normalizes the list and enforces the cap atomically per command,
        // so concurrent moderators can't lose each other's pins; the pipeline surfaces its errors.
        var setPinnedCmd = new Chats_SetPinned {
            Session = Session,
            EntryId = entryId,
            MustPin = mustPin,
        };
        return UICommander.Run(setPinnedCmd);
    }

    private Task<UIActionResult<Unit>> SetPersonal(ChatEntryId entryId, bool mustPin)
    {
        var command = LocalCommand.New(async () => {
            var settings = UserSettingsUI.ChatUserPinnedEntries(entryId.ChatId);
            var stored = await settings.Get().ConfigureAwait(false);
            var entryIds = await ChatPinnedEntries.Apply(
                stored.EntryIds,
                entryId,
                mustPin,
                ChatUserPinnedEntries.MaxCount,
                IsPresent,
                CancellationToken.None
                ).ConfigureAwait(false);
            // An empty list is stored rather than null: Temporals keep only non-null values,
            // so a null would leave the optimistic copy showing the last pin.
            await settings.Set(new ChatUserPinnedEntries { EntryIds = entryIds }).ConfigureAwait(false);
        });
        return UICommander.Run(command, CancellationToken.None);

        async Task<bool> IsPresent(ChatEntryId id, CancellationToken ct) {
            var entry = await Chats.GetEntry(Session, id, ct).ConfigureAwait(false);
            return entry is { IsRemoved: false };
        }
    }
}

public sealed record ChatPin(ChatEntryId EntryId, bool IsShared, bool IsPersonal)
{
    public bool IsPinned => IsShared || IsPersonal;
}
