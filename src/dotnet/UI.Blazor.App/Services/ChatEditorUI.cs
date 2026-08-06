using ActualChat.UI.Blazor.App.Events;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatEditorUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private readonly Lock _lock = new();
    private readonly MutableState<RelatedEntryRef?> _relatedEntryRef;

    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;

    // ReSharper disable once InconsistentlySynchronizedField
    public IState<RelatedEntryRef?> RelatedEntryRef => _relatedEntryRef;

    public ChatEditorUI(AppUIHub hub) : base(hub)
        => _relatedEntryRef = StateFactory.NewMutable(
            (RelatedEntryRef?)null,
            StateCategories.Get(GetType(), nameof(RelatedEntryRef)));

    void INotifyInitialized.Initialized()
        => this.Start();

    public Task ShowRelatedEntry(RelatedEntryKind kind, ChatEntryId entryId, bool focusOnEditor, bool updateUI = true)
        => ShowRelatedEntry(new RelatedEntryRef(kind, new EntryRef(entryId)), focusOnEditor, updateUI);

    public Task ShowRelatedEntry(RelatedEntryKind kind, ChatEntry chatEntry, bool focusOnEditor, bool updateUI = true)
    {
        var entryRef = new EntryRef(chatEntry.Id);
        if (chatEntry.IsSending && !chatEntry.HasVersion())
            entryRef = entryRef with {
                ChatEntry = chatEntry
            };
        return ShowRelatedEntry(new RelatedEntryRef(kind, entryRef), focusOnEditor, updateUI);
    }

    public async Task ShowRelatedEntry(RelatedEntryRef relatedEntryRef, bool focusOnEditor, bool updateUI = true)
    {
        lock (_lock) {
            if (_relatedEntryRef.Value == relatedEntryRef)
                return;

            _relatedEntryRef.Value = relatedEntryRef;
        }

        if (focusOnEditor)
            _ = UIEventHub.Publish<FocusChatMessageEditorEvent>();
        if (updateUI)
            _ = UICommander.RunNothing();
        PlayTune();
        if (relatedEntryRef.EntryRef.ChatEntry is null)
            await SaveRelatedEntry(relatedEntryRef.EntryId.ChatId, relatedEntryRef).ConfigureAwait(false);

        void PlayTune()
        {
            var tune = relatedEntryRef.Kind switch
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
        RelatedEntryRef? oldEntryRef;
        lock (_lock) {
            if (_relatedEntryRef.Value == null)
                return;

            oldEntryRef = _relatedEntryRef.Value;
            _relatedEntryRef.Value = null;
        }
        if (updateUI)
            _ = UICommander.RunNothing();
        _ = TuneUI.Play(Tune.CancelReply);
        if (oldEntryRef != null)
            await SaveRelatedEntry(oldEntryRef.EntryId.ChatId, null).ConfigureAwait(false);
    }

    public Task Edit(ChatEntry chatEntry, CancellationToken cancellationToken = default)
        => UIEventHub.Publish(new EditChatMessageEvent(chatEntry), cancellationToken);

    public async Task EditLast(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var author = await Authors.GetOwn(Session, chatId, CancellationToken.None).ConfigureAwait(false);
        if (author == null)
            return;

        ChatEntry? lastEditableEntry = null;
        using (ComputedSynchronizer.Default.Activate()) {
            var chatIdRange = await Chats
                .GetIdRange(Session, chatId, CancellationToken.None)
                .ConfigureAwait(false);

            var chatSendingMessages = Hub.SendingMessages.GetSendingMessages(chatId);
            var newMessages = await chatSendingMessages.GetNewMessages(author.Id, chatIdRange.End).ConfigureAwait(false);
            if (newMessages.Length > 0) {
                lastEditableEntry = newMessages[^1];
                var sendingMessage = lastEditableEntry.GetSendingMessage();
                // Skip messages already confirmed within the server
                if (sendingMessage is not null && sendingMessage.PostedChatEntry is not null)
                    lastEditableEntry = null;
            }
            if (lastEditableEntry is null) {
                var chatEntryReader = Hub.NewEntryReader(chatId);
                lastEditableEntry = await chatEntryReader.GetLast(
                        chatIdRange,
                        x => x.AuthorId == author.Id
                            && x is {
                                HasAudio: false,
                                IsContentStreaming: false,
                                Forwarded: null,
                            },
                        1000, // Max. 1000 entries to scan upwards
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        if (lastEditableEntry == null)
            return;

        await Edit(lastEditableEntry, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreRelatedEntry(ChatId chatId)
    {
        var relatedEntry = await LocalSettings.GetDraftRelatedEntryRef(chatId).ConfigureAwait(false);
        lock (_lock)
            _relatedEntryRef.Value = relatedEntry;
    }

    // Private methods

    private Task SaveRelatedEntry(ChatId chatId, RelatedEntryRef? relatedChatEntry)
        => LocalSettings.SetDraftRelatedEntryRef(chatId, relatedChatEntry);
}
