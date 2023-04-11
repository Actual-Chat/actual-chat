using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatMessagesUI : WorkerBase, IComputeService
{
    private readonly ConcurrentQueue<EntryChange> _queue = new ();
    private readonly SemaphoreSlim _semaphore = new (0, 1);
    private SyncedStateLease<ChatPosition>? ReadPositionState { get; set; } = null!;
    private Session Session { get; }
    private ChatEditorUI ChatEditorUI { get; }
    private ChatAttachmentsUI ChatAttachmentsUI { get; }
    private ErrorUI ErrorUI { get; }
    private UICommander UICommander { get; }
    private ILogger Log { get; }
    public ChatMessagesUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        ChatEditorUI = services.GetRequiredService<ChatEditorUI>();
        ChatAttachmentsUI = services.GetRequiredService<ChatAttachmentsUI>();
        ErrorUI = services.GetRequiredService<ErrorUI>();
        UICommander = services.UICommander();
        Log = services.LogFor<ChatMessagesUI>();
    }

    public async Task EnqueueForPosting(ChatId chatId, string text)
    {
        var attachments = ChatAttachmentsUI.PopAll();
        var relatedChatEntry = await PopRelatedEntry().ConfigureAwait(false);
        if (attachments.IsEmpty && text.IsNullOrEmpty())
            return;

        try {
            _queue.Enqueue(new (EntryChangeKind.Post, chatId, text, attachments, relatedChatEntry));
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
            switch (change.Kind) {
            case EntryChangeKind.Post:
                await Post(change.ChatId, change.Text, change.Attachments, change.RelatedChatEntry, cancellationToken).ConfigureAwait(false);
                break;
            case EntryChangeKind.Remove:
                // TODO: support message remove queue
                throw new NotImplementedException();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change.Kind), change.Kind, null);
            }
            _queue.TryDequeue(out _);
        }
    }

    private async Task Post(
        ChatId chatId,
        string text,
        ImmutableArray<Attachment> attachments,
        RelatedChatEntry? relatedEntry,
        CancellationToken cancellationToken)
    {
        IChats.UpsertTextEntryCommand cmd;
        if (relatedEntry is { Kind: RelatedEntryKind.Edit } vRelated) {
            if (vRelated.Id.IsNone)
                throw StandardError.Constraint("Invalid ChatUI.RelatedChatEntry value.");

            cmd = new IChats.UpsertTextEntryCommand(Session, vRelated.Id.ChatId, vRelated.Id.LocalId, text);
        }
        else {
            cmd = new IChats.UpsertTextEntryCommand(Session, chatId,  null, text) {
                RepliedChatEntryId = relatedEntry?.Id.LocalId,
            };
        }
        var entry = await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);

        if (ReadPositionState != null && ReadPositionState.Value.EntryLid < readEntryLid)
            ReadPositionState.Value = new ChatPosition(readEntryLid);
    }

    private sealed record EntryChange(EntryChangeKind Kind, ChatId ChatId, string Text, ImmutableArray<Attachment> Attachments, RelatedChatEntry? RelatedChatEntry);

    private enum EntryChangeKind {
        Post,
        Remove,
    }
}
