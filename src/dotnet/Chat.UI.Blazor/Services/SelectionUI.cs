using ActualChat.UI.Blazor.Services;
using Cysharp.Text;

namespace ActualChat.Chat.UI.Blazor.Services;

public class SelectionUI : ScopedServiceBase
{
    private readonly IMutableState<ImmutableHashSet<ChatEntryId>> _selection;
    private readonly IMutableState<bool> _hasSelection;

    private ChatHub ChatHub { get; }
    private IChats Chats => ChatHub.Chats;
    private KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory => ChatHub.ChatMarkupHubFactory;
    private ModalUI ModalUI => ChatHub.ModalUI;
    private ToastUI ToastUI => ChatHub.ToastUI;
    private ClipboardUI ClipboardUI => ChatHub.ClipboardUI;
    private UICommander UICommander => ChatHub.UICommander();

    public IState<bool> HasSelection => _hasSelection;
    public IState<ImmutableHashSet<ChatEntryId>> Selection => _selection;

    public SelectionUI(ChatHub chatHub) : base(chatHub.Scope())
    {
        ChatHub = chatHub;

        var type = GetType();
        _selection = StateFactory.NewMutable(
            ImmutableHashSet<ChatEntryId>.Empty,
            StateCategories.Get(type, nameof(Selection)));
        _hasSelection = StateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(HasSelection)));
        _selection.Updated += (state, _) => _hasSelection.Value = state.Value.Count != 0;
    }

    public bool IsSelected(ChatEntryId chatEntryId)
        => _selection.Value.Contains(chatEntryId);

    public void Select(ChatEntryId chatEntryId)
        => _selection.Set(chatEntryId, static (chatEntryId1, x) => x.Value.Add(chatEntryId1));

    public void Unselect(ChatEntryId chatEntryId)
        => _selection.Set(chatEntryId, static (chatEntryId1, x) => x.Value.Remove(chatEntryId1));

    public void Clear()
        => _selection.Set(static x => x.Value.Clear());

    // Actions

    public Task CopyToClipboard(ChatEntryId chatEntryId)
        => CopyToClipboard(ImmutableHashSet.Create(chatEntryId));
    public async Task CopyToClipboard(IReadOnlySet<ChatEntryId>? selection = null) {
        selection ??= Selection.Value;
        if (selection.Count == 0)
            return;

        var chatId = selection.First().ChatId;
        var chatMarkupHub = ChatMarkupHubFactory[chatId];

        using var sb = ZString.CreateStringBuilder();
        foreach (var chatEntryId in selection.OrderBy(x => x.LocalId)) {
            var chatEntry = await Chats.GetEntry(Session, chatEntryId).ConfigureAwait(false);
            if (chatEntry == null || chatEntry.Content.IsNullOrEmpty())
                continue;

            var markup = await chatMarkupHub
                .GetMarkup(chatEntry, MarkupConsumer.MessageView, default)
                .ConfigureAwait(false);
            var text = markup.ToClipboardText();
            sb.AppendLine(text);
        }

        await Task.CompletedTask.ConfigureAwait(true); // Get back to the Blazor Dispatcher
        await ClipboardUI.WriteText(sb.ToString()).ConfigureAwait(true);
        Clear();
    }

    public Task Delete(ChatEntryId chatEntryId)
        => Delete(ImmutableHashSet.Create(chatEntryId));
    public async Task Delete(IReadOnlySet<ChatEntryId>? selection = null) {
        selection ??= Selection.Value;
        if (selection.Count == 0)
            return;

        var chatId = selection.Select(x => x.ChatId).First();
        var localIds = selection.Select(x => x.LocalId).ToApiArray();
        var removeCommand = new Chats_RemoveTextEntries(Session, chatId, localIds);
        await UICommander.Run(removeCommand).ConfigureAwait(true);

        ToastUI.Show("Messages deleted", Restore, "Undo", ToastDismissDelay.Long);
        Clear();

        void Restore() {
            var restoreCommand = new Chats_RestoreTextEntries(Session, chatId, localIds);
            _ = UICommander.Run(restoreCommand);
        }
    }

    public Task Forward(ChatEntryId chatEntryId)
        => Forward(ImmutableHashSet.Create(chatEntryId));
    public async Task Forward(IReadOnlySet<ChatEntryId>? selection = null) {
        selection ??= Selection.Value;
        if (selection.Count == 0)
            return;

        var chatId = selection.First().ChatId;
        var modalModel = new ForwardMessageModal.Model(chatId);
        await (await ModalUI.Show(modalModel).ConfigureAwait(true)).WhenClosed.ConfigureAwait(true);
        var selectedChatIds = modalModel.SelectedChatIds;
        if (selectedChatIds.Count == 0)
            return;

        var cmd = new Chats_ForwardTextEntries(
            Session,
            chatId,
            selection.ToApiArray(),
            selectedChatIds.ToApiArray());
        await UICommander.Run(cmd, CancellationToken.None).ConfigureAwait(true);
        Clear();
    }
}
