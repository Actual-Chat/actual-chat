using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatEditorUI
{
    private readonly object _lock = new();
    private readonly IMutableState<RelatedChatEntry?> _relatedChatEntry;

    private Session Session { get; }
    private IAuthors Authors { get; }
    private IChats Chats { get; }
    private TuneUI TuneUI { get; }
    private UICommander UICommander { get; }
    private UIEventHub UIEventHub { get; }
    public IState<RelatedChatEntry?> RelatedChatEntry => _relatedChatEntry;

    public ChatEditorUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Authors = services.GetRequiredService<IAuthors>();
        Chats = services.GetRequiredService<IChats>();
        TuneUI = services.GetRequiredService<TuneUI>();
        UICommander = services.UICommander();
        UIEventHub = services.UIEventHub();

        var type = GetType();
        _relatedChatEntry = services.StateFactory().NewMutable(
            (RelatedChatEntry?)null,
            StateCategories.Get(type, nameof(RelatedChatEntry)));
    }

    public void ShowRelatedEntry(RelatedEntryKind kind, ChatEntryId entryId, bool focusOnEditor, bool updateUI = true)
    {
        var relatedChatEntry = new RelatedChatEntry(kind, entryId);
        lock (_lock) {
            if (_relatedChatEntry.Value == relatedChatEntry)
                return;

            _relatedChatEntry.Value = relatedChatEntry;
        }
        if (focusOnEditor)
            _ = UIEventHub.Publish<FocusChatMessageEditorEvent>();
        if (updateUI)
            _ = UICommander.RunNothing();

        var tuneName = kind switch {
            RelatedEntryKind.Reply => "reply-message",
            RelatedEntryKind.Edit => "edit-message",
            _ => "",
        };
        if (!tuneName.IsNullOrEmpty())
            TuneUI.Play(tuneName);
    }

    public void HideRelatedEntry(bool updateUI = true)
    {
        lock (_lock) {
            if (_relatedChatEntry.Value == null)
                return;

            _relatedChatEntry.Value = null;
        }
        if (updateUI)
            _ = UICommander.RunNothing();
        _ = TuneUI.Play("cancel");
    }

    public Task Edit(ChatEntry chatEntry, CancellationToken cancellationToken = default)
        => UIEventHub.Publish(new EditChatMessageEvent(chatEntry), cancellationToken);

    public async Task EditLast(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (chatId.IsNone)
            return;

        var author = await Authors.GetOwn(Session, chatId, CancellationToken.None).ConfigureAwait(false);
        if (author == null)
            return;

        var chatIdRange = await Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, CancellationToken.None).ConfigureAwait(false);
        var idTileLayer = ChatEntryReader.IdTileStack.Layers[1]; // 5*4 = scan by 20 entries
        var chatEntryReader = Chats.NewEntryReader(Session, chatId, ChatEntryKind.Text, idTileLayer);
        var lastEditableEntry = await chatEntryReader.GetLast(
                chatIdRange,
                x => x.AuthorId == author.Id && x is { HasMediaEntry: false, IsStreaming: false },
                1000, // Max. 1000 entries to scan upwards
                CancellationToken.None)
            .ConfigureAwait(false);
        if (lastEditableEntry == null)
            return;

        await Edit(lastEditableEntry, cancellationToken).ConfigureAwait(false);
    }
}
