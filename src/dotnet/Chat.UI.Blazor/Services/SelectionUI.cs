using System.Text;
using ActualChat.UI.Blazor.Services;
using Cysharp.Text;
using Microsoft.Extensions.Primitives;

namespace ActualChat.Chat.UI.Blazor.Services;

public class SelectionUI : ScopedServiceBase<ChatUIHub>
{
    private readonly IMutableState<ImmutableHashSet<ChatEntryId>> _selection;
    private readonly IMutableState<bool> _hasSelection;

    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    private DateTimeConverter DateTimeConverter => Hub.DateTimeConverter;
    private KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory => Hub.ChatMarkupHubFactory;
    private ModalUI ModalUI => Hub.ModalUI;
    private History History => Hub.History;
    private ToastUI ToastUI => Hub.ToastUI;
    private ClipboardUI ClipboardUI => Hub.ClipboardUI;
    private UICommander UICommander => Hub.UICommander();

    public IState<bool> HasSelection => _hasSelection;
    public IState<ImmutableHashSet<ChatEntryId>> Selection => _selection;

    public SelectionUI(ChatUIHub hub) : base(hub)
    {
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

        var showAuthor = selection.Count > 1;
        var chatId = selection.First().ChatId;
        var chatMarkupHub = ChatMarkupHubFactory[chatId];

        using var sb = ZString.CreateStringBuilder();
        var currentAuthor = AuthorId.None;
        foreach (var chatEntryId in selection.OrderBy(x => x.LocalId)) {
            var chatEntry = await Chats.GetEntry(Session, chatEntryId).ConfigureAwait(false);
            if (chatEntry == null || chatEntry.Content.IsNullOrEmpty())
                continue;

            var markup = await chatMarkupHub
                .GetMarkup(chatEntry, MarkupConsumer.MessageView, default)
                .ConfigureAwait(false);

            if (showAuthor && currentAuthor != chatEntry.AuthorId) {
                if (sb.Length > 0)
                    sb.AppendLine();
                currentAuthor = chatEntry.AuthorId;
                var author = await Authors.Get(Session, chatEntry.ChatId, chatEntry.AuthorId, default).ConfigureAwait(false);
                var authorName = author?.Avatar.Name ?? "(N/A)";
                var timestamp = DateTimeConverter.ToLocalTime(chatEntry.BeginsAt).ToString("g", CultureInfo.InvariantCulture);
                sb.AppendFormat("{0}, [{1}]", authorName, timestamp);
                sb.AppendLine();
            }

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
        var firstChatId = selectedChatIds.First();
        var info = await BuildInfoMessage().ConfigureAwait(true);
        ToastUI.Show(info, NavigateAction, "Navigate", ToastDismissDelay.Long);
        Clear();
        return;

        void NavigateAction()
            => _ = History.NavigateTo(Links.Chat(firstChatId));

        async Task<string> BuildInfoMessage()
        {
            var sb = new StringBuilder();
            sb.Append("Forwarded ");
            sb.Append(selection.Count);
            sb.Append(' ');
            sb.Append("message".Pluralize(selection.Count));
            sb.Append(" to ");
            Chat? chat = null;

            if (selectedChatIds.Count == 1)
                chat = await Chats.Get(Session, firstChatId, default).ConfigureAwait(true);
            if (chat != null) {
                sb.Append('\'');
                sb.Append(chat.Title);
                sb.Append("\' chat");
            }
            else {
                sb.Append(selectedChatIds.Count);
                sb.Append(" chats");
            }
            return sb.ToString();
        }
    }
}
