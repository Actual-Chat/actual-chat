using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.UI.Blazor.Services;
using Stl.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatEditorUI : WorkerBase, IComputeService, INotifyInitialized
{
    private readonly object _lock = new();
    private readonly IMutableState<RelatedChatEntry?> _relatedChatEntry;
    private IAuthors? _authors;
    private IChats? _chats;
    private LocalSettings? _localSettings;
    private TuneUI? _tuneUI;
    private UICommander? _uiCommander;
    private UIEventHub? _uiEventHub;

    private IServiceProvider Services { get; }
    private Session Session { get; }

    private IAuthors Authors => _authors ??= Services.GetRequiredService<IAuthors>();
    private IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    private LocalSettings LocalSettings => _localSettings ??= Services.GetRequiredService<LocalSettings>();
    private TuneUI TuneUI => _tuneUI ??= Services.GetRequiredService<TuneUI>();
    private UICommander UICommander => _uiCommander ??= Services.UICommander();
    private UIEventHub UIEventHub => _uiEventHub ??= Services.UIEventHub();

    // ReSharper disable once InconsistentlySynchronizedField
    public IState<RelatedChatEntry?> RelatedChatEntry => _relatedChatEntry;

    public ChatEditorUI(IServiceProvider services)
    {
        Services = services;
        Session = services.Session();

        var type = GetType();
        _relatedChatEntry = services.StateFactory().NewMutable(
            (RelatedChatEntry?)null,
            StateCategories.Get(type, nameof(RelatedChatEntry)));
    }

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
        _ = PlayTune();
        await SaveRelatedEntry(relatedChatEntry.Id.ChatId, relatedChatEntry);

        ValueTask PlayTune()
        {
            var tuneName = relatedChatEntry.Kind switch
            {
                RelatedEntryKind.Reply => "reply-message",
                RelatedEntryKind.Edit => "edit-message",
                _ => "",
            };
            return !tuneName.IsNullOrEmpty() ? TuneUI.Play(tuneName) : default;
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
        _ = TuneUI.Play("cancel");
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

    private Task SaveRelatedEntry(ChatId chatId, RelatedChatEntry? relatedChatEntry)
        => LocalSettings.SetDraftRelatedEntry(chatId, relatedChatEntry);

    public async Task RestoreRelatedEntry(ChatId chatId)
    {
        if (chatId.IsNone)
            return;

        var relatedEntry = await LocalSettings.GetDraftRelatedEntry(chatId).ConfigureAwait(false);
        lock (_lock)
            _relatedChatEntry.Value = relatedEntry;
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => HideWhenRelatedEntryRemoved(cancellationToken);

    private async Task HideWhenRelatedEntryRemoved(CancellationToken cancellationToken)
    {
        var cRelatedChatEntry = await Computed.Capture(() => GetRelatedChatEntry(cancellationToken)).ConfigureAwait(false);
        await foreach (var change in cRelatedChatEntry.Changes(cancellationToken).ConfigureAwait(false))
        {
            var (chatEntryLink, chatEntry) = change.Value;
            if (chatEntryLink != null && chatEntry == null)
                await HideRelatedEntry().ConfigureAwait(false);
        }
    }

    [ComputeMethod]
    protected virtual async Task<(RelatedChatEntry?, ChatEntry?)> GetRelatedChatEntry(CancellationToken cancellationToken)
    {
        var entryLink = await RelatedChatEntry.Use(cancellationToken).ConfigureAwait(false);
        if (entryLink == null)
            return (null, null);

        var entry = await Chats.GetEntry(Session, entryLink.Value.Id, cancellationToken).ConfigureAwait(false);
        return (entryLink, entry);
    }
}
