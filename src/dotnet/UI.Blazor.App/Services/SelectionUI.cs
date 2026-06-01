using System.Net;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class SelectionUI : UIServiceBase<AppUIHub>
{
    // Serializes markup back to its storable text form (`@`name`id`, **bold**, …) for the clipboard's
    // hidden data-voxt-markup payload, which the editor reconstructs on paste.
    private static readonly MarkupFormatter MarkupTextFormatter = new();

    private readonly MutableState<ImmutableHashSet<ChatEntryId>> _selection;
    private readonly MutableState<bool> _hasSelection;

    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    private KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory => Hub.ChatMarkupHubFactory;
    private ClipboardUI ClipboardUI => Hub.ClipboardUI;
    private TranslationUI TranslationUI => Hub.TranslationUI;

    public IState<bool> HasSelection => _hasSelection;
    public IState<ImmutableHashSet<ChatEntryId>> Selection => _selection;

    public SelectionUI(AppUIHub hub) : base(hub)
    {
        var type = GetType();
        _selection = StateFactory.NewMutable(
            ImmutableHashSet<ChatEntryId>.Empty,
            StateCategories.Get(type, nameof(Selection)));
        _hasSelection = StateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(HasSelection)));
        _selection.Updated += (_, _) => {
            _hasSelection.Value = _selection.Value.Count != 0;
        };
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

        var (plain, html) = await GetTextToCopy(selection).ConfigureAwait(true); // Get back to the Blazor Dispatcher
        await ClipboardUI.WriteText(plain, html).ConfigureAwait(true);
        Clear();
    }
    public async Task CopyToClipboard(ChatEntry sendingChatEntry)
    {
        if (!sendingChatEntry.IsSending)
            throw new ArgumentOutOfRangeException(nameof(sendingChatEntry), "Given chat entry should be a sending chat entry.");
        var (plain, html) = await GetTextToCopy(sendingChatEntry).ConfigureAwait(true); // Get back to the Blazor Dispatcher
        await ClipboardUI.WriteText(plain, html).ConfigureAwait(true);
    }

    // Returns (plain text shown to external apps, HTML carrying the original markup for our editor).
    private static string BuildClipboardHtml(string plainText, string markup)
    {
        var visible = WebUtility.HtmlEncode(plainText).Replace("\n", "<br>");
        var data = WebUtility.HtmlEncode(markup);
        return $"<div data-voxt-markup=\"{data}\">{visible}</div>";
    }

    private async Task<(string Plain, string Html)> GetTextToCopy(IReadOnlySet<ChatEntryId> selection)
    {
        var showAuthor = selection.Count > 1;
        var chatId = selection.First().ChatId;
        var chatMarkupHub = ChatMarkupHubFactory[chatId];
        CancellationToken cancellationToken = default;

        var lines = new List<string>();
        var markupLines = new List<string>();
        AuthorId? currentAuthorId = null;
        foreach (var chatEntryId in selection.OrderBy(x => x.LocalId)) {
            var chatEntry = await Chats.GetEntry(Session, chatEntryId, cancellationToken).ConfigureAwait(false);
            if (chatEntry == null || chatEntry.Content.IsNullOrEmpty())
                continue;

            var mustTranslate = await TranslationUI.MustTranslate(chatEntry, false, cancellationToken).ConfigureAwait(false);
            Translation? translation = null;
            if (mustTranslate) {
                translation = await TranslationUI.GetExisting(chatEntry.Id, cancellationToken).ConfigureAwait(false);
                if (translation is not null && translation.MatchesOriginal(chatEntry.Content))
                    translation = null;
            }

            var markup = await chatMarkupHub
                .GetMarkup(chatEntry, translation, MarkupConsumer.MessageView, cancellationToken)
                .ConfigureAwait(false);
            markup = await chatMarkupHub.ApplyMentionResolver(markup, cancellationToken).ConfigureAwait(false);

            if (showAuthor && currentAuthorId != chatEntry.AuthorId) {
                if (lines.Count > 0)
                    lines.Add("");
                currentAuthorId = chatEntry.AuthorId;
                var author = await Authors.Get(Session, chatEntry.ChatId, chatEntry.AuthorId, cancellationToken).ConfigureAwait(false);
                var authorName = author?.Avatar.Name ?? "(N/A)";
                var timestamp = DateTimeConverter.ToLocalTime(chatEntry.BeginsAt).ToString("g");
                lines.Add($"{authorName}, [{timestamp}]");
            }

            lines.Add(markup.ToClipboardText());
            markupLines.Add(MarkupTextFormatter.Format(markup));
        }

        var plain = string.Join('\n', lines);
        return (plain, BuildClipboardHtml(plain, string.Join('\n', markupLines)));
    }

    private async Task<(string Plain, string Html)> GetTextToCopy(ChatEntry sendingChatEntry)
    {
        var chatId = sendingChatEntry.ChatId;
        var chatMarkupHub = ChatMarkupHubFactory[chatId];
        CancellationToken cancellationToken = default;
        var markup = await chatMarkupHub
            .GetMarkup(sendingChatEntry, null, MarkupConsumer.MessageView, cancellationToken)
            .ConfigureAwait(false);
        markup = await chatMarkupHub.ApplyMentionResolver(markup, cancellationToken).ConfigureAwait(false);
        var plain = markup.ToClipboardText();
        return (plain, BuildClipboardHtml(plain, MarkupTextFormatter.Format(markup)));
    }

    public Task Delete(ChatEntryId chatEntryId)
        => Delete(ImmutableHashSet.Create(chatEntryId));
    public async Task Delete(IReadOnlySet<ChatEntryId>? selection = null) {
        selection ??= Selection.Value;
        if (selection.Count == 0)
            return;

        var chatId = selection.Select(x => x.ChatId).First();
        var localIds = selection.Select(x => x.LocalId).ToArray();

        var otherAuthorCount = await GetOtherAuthorEntryCount(chatId, selection).ConfigureAwait(true);
        if (otherAuthorCount > 0) {
            var word = "message".Pluralize(otherAuthorCount);
            var confirmed = false;
            var model = new ConfirmModal.Model(true,
                $"You're about to delete {otherAuthorCount} {word} written by other users. Continue?",
                () => { confirmed = true; }) {
                Title = $"Delete {word}?",
                ConfirmButtonText = "Delete",
            };
            var modalRef = await ModalUI.Show(model).ConfigureAwait(true);
            await modalRef.WhenClosed.ConfigureAwait(true);
            if (!confirmed)
                return;
        }

        var removeCommand = new Chats_RemoveEntries(Session, chatId, localIds);
        await UICommander.Run(removeCommand).ConfigureAwait(true);

        ToastUI.Show("Messages deleted", Restore, "Undo", ToastDismissDelay.Long);
        Clear();

        void Restore() {
            var restoreCommand = new Chats_RestoreEntries(Session, chatId, localIds);
            _ = UICommander.Run(restoreCommand);
        }
    }

    private async Task<int> GetOtherAuthorEntryCount(ChatId chatId, IReadOnlySet<ChatEntryId> selection)
    {
        var ownAuthor = await Authors.GetOwn(Session, chatId, default).ConfigureAwait(false);
        var ownAuthorId = ownAuthor?.Id ?? default;
        var count = 0;
        foreach (var chatEntryId in selection) {
            var chatEntry = await Chats.GetEntry(Session, chatEntryId).ConfigureAwait(false);
            if (chatEntry != null && chatEntry.AuthorId != ownAuthorId)
                count++;
        }
        return count;
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

        var cmd = new Chats_ForwardEntries(
            Session,
            chatId,
            selection.ToArray(),
            selectedChatIds.ToArray());
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
            var sb = ActualLab.Text.StringBuilderExt.Acquire();
            sb.Append("Forwarded ");
            sb.Append(selection.Count);
            sb.Append(' ');
            sb.Append("message".Pluralize(selection.Count));
            sb.Append(" to ");
            Chat.Chat? chat = null;

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
            return sb.ToStringAndRelease();
        }
    }

    public Task StartThread(ChatEntryId chatEntryId)
        => StartThread(ImmutableHashSet.Create(chatEntryId));
    public async Task StartThread(IReadOnlySet<ChatEntryId>? selection = null) {
        selection ??= Selection.Value;
        if (selection.Count == 0)
            return;

        var chatId = selection.First().ChatId;
        var textEntryIds = selection.OrderBy(c => c.LocalId).ToArray();
        var modalModel = new NewThreadModal.Model(chatId, textEntryIds);
        await (await ModalUI.Show(modalModel).ConfigureAwait(true)).WhenClosed.ConfigureAwait(true);
        if (modalModel.Title.IsNullOrEmpty())
            return;

        var cmd = new ChatThreads_Start(
            Session,
            chatId,
            modalModel.Title,
            modalModel.Description,
            textEntryIds);
        await UICommander.Call(cmd, CancellationToken.None).ConfigureAwait(true);
        Clear();
    }
}
