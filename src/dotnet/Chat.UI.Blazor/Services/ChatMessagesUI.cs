using Stl.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatMessagesUI : WorkerBase, IComputeService, INotifyInitialized
{
    private readonly ConcurrentQueue<EntryChange> _queue = new ();
    private readonly SemaphoreSlim _semaphore = new (0, 1);
    private Session Session { get; }
    private ChatEditorUI ChatEditorUI { get; }
    private ChatAttachmentsUI ChatAttachmentsUI { get; }
    private UICommander UICommander { get; }
    private ILogger Log { get; }
    public ChatMessagesUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        ChatEditorUI = services.GetRequiredService<ChatEditorUI>();
        ChatAttachmentsUI = services.GetRequiredService<ChatAttachmentsUI>();
        UICommander = services.UICommander();
        Log = services.LogFor<ChatMessagesUI>();
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public async Task EnqueueForPosting(ChatId chatId, string text)
    {
        var attachments = ChatAttachmentsUI.PopAll();
        var relatedChatEntry = await PopRelatedEntry().ConfigureAwait(false);
        if (attachments.IsEmpty && text.IsNullOrEmpty())
            return;

        try {
            _queue.Enqueue(new (chatId, text, attachments, relatedChatEntry));
            _semaphore.Release();
        }
        catch (Exception) {
            ChatAttachmentsUI.RestoreEditorAttachments(attachments);
            throw;
        }
    }

    private async Task<RelatedChatEntry?> PopRelatedEntry()
    {
        var relatedChatEntry = ChatEditorUI.RelatedChatEntry.Value;
        await ChatEditorUI.HideRelatedEntry(false);
        return relatedChatEntry;
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new AsyncChain[] {
            new(nameof(DispatchQueue), DispatchQueue),
        };
        var retryDelays = new RetryDelaySeq(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
                .CycleForever()
            ).RunIsolated(cancellationToken);
    }

    private async Task DispatchQueue(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        // TODO: continue if some entry fails
        while (_queue.TryPeek(out var change)) {
            var (chatId, text, attachments, relatedEntry) = change;
            IChats.UpsertTextEntryCommand cmd;
            cmd = relatedEntry is { Kind: RelatedEntryKind.Edit } vRelated
                ? new IChats.UpsertTextEntryCommand(Session, vRelated.Id.ChatId, vRelated.Id.LocalId, text)
                : new IChats.UpsertTextEntryCommand(Session, chatId, null, text) {
                    RepliedChatEntryId = relatedEntry?.Id.LocalId,
                };
            var entry = await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);
            // TODO: update LastReadEntry if there were no incoming messages
            // if (ReadPositionState != null && ReadPositionState.Value.EntryLid < readEntryLid)
            //     ReadPositionState.Value = new ChatPosition(readEntryLid);
            _queue.TryDequeue(out _);
        }
    }

    private sealed record EntryChange(ChatId ChatId, string Text, ImmutableArray<Attachment> Attachments, RelatedChatEntry? RelatedChatEntry);
}
