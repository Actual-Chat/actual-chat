using ActualChat.UI.Blazor.App.Events;
using ActualChat.UI.Blazor.Services;
using ActualLab.Fusion.Client;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatEditorUI : ScopedWorkerBase<ChatUIHub>, IComputeService, INotifyInitialized
{
    private readonly object _lock = new();
    private readonly MutableState<RelatedChatEntry?> _relatedChatEntry;

    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    private TuneUI TuneUI => Hub.TuneUI;
    private UICommander UICommander => Hub.UICommander();
    private UIEventHub UIEventHub => Hub.UIEventHub();

    // ReSharper disable once InconsistentlySynchronizedField
    public IState<RelatedChatEntry?> RelatedChatEntry => _relatedChatEntry;

    public ChatEditorUI(ChatUIHub hub) : base(hub)
        => _relatedChatEntry = StateFactory.NewMutable(
            (RelatedChatEntry?)null,
            StateCategories.Get(GetType(), nameof(RelatedChatEntry)));

    void INotifyInitialized.Initialized()
        => this.Start();

    public Task ShowRelatedEntry(RelatedEntryKind kind, ChatEntryId entryId, bool focusOnEditor, bool updateUI = true)
        => ShowRelatedEntry(new RelatedChatEntry(kind, entryId), focusOnEditor, updateUI);

    public async Task ShowRelatedEntry(RelatedChatEntry relatedChatEntry, bool focusOnEditor, bool updateUI = true)
    {
        lock (_lock)
        {
            if (_relatedChatEntry.Value == relatedChatEntry)
                return;

            _relatedChatEntry.Value = relatedChatEntry;
        }

        if (focusOnEditor)
            _ = UIEventHub.Publish<FocusChatMessageEditorEvent>();
        if (updateUI)
            _ = UICommander.RunNothing();
        PlayTune();
        await SaveRelatedEntry(relatedChatEntry.Id.ChatId, relatedChatEntry).ConfigureAwait(false);

        void PlayTune()
        {
            var tune = relatedChatEntry.Kind switch
            {
                RelatedEntryKind.Reply => Tune.ReplyMessage,
                RelatedEntryKind.Edit => Tune.EditMessage,
                _ => Tune.None,
            };
            _ = TuneUI.Play(tune);
        }
    }

    public async Task HideRelatedEntry(bool updateUI = true)
    {
        RelatedChatEntry? old;
        lock (_lock) {
            if (_relatedChatEntry.Value == null)
                return;

            old = _relatedChatEntry.Value;
            _relatedChatEntry.Value = null;
        }
        if (updateUI)
            _ = UICommander.RunNothing();
        _ = TuneUI.Play(Tune.CancelReply);
        if (old != null)
            await SaveRelatedEntry(old.Value.Id.ChatId, null).ConfigureAwait(false);
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

        ChatEntry? lastEditableEntry;
        using (RemoteComputedSynchronizer.Default.Activate()) {
            var chatIdRange = await Chats
                .GetIdRange(Session, chatId, ChatEntryKind.Text, CancellationToken.None)
                .ConfigureAwait(false);
            var chatEntryReader = Hub.NewEntryReader(chatId, ChatEntryKind.Text);
            lastEditableEntry = await chatEntryReader.GetLast(
                    chatIdRange,
                    x => x.AuthorId == author.Id && x is {
                        HasMediaEntry: false,
                        IsStreaming: false,
                        ForwardedChatEntryId.IsNone: true,
                    },
                    1000, // Max. 1000 entries to scan upwards
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (lastEditableEntry == null)
                return;
        }

        await Edit(lastEditableEntry, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreRelatedEntry(ChatId chatId)
    {
        if (chatId.IsNone)
            return;

        var relatedEntry = await LocalSettings.GetDraftRelatedEntry(chatId).ConfigureAwait(false);
        lock (_lock)
            _relatedChatEntry.Value = relatedEntry;
    }

    // Private methods

    private Task SaveRelatedEntry(ChatId chatId, RelatedChatEntry? relatedChatEntry)
        => LocalSettings.SetDraftRelatedEntry(chatId, relatedChatEntry);
}
