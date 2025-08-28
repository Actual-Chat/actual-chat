using ActualChat.Hashing;
using ActualChat.Messaging;
using ActualChat.UI.Blazor.Services;
using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

public class SendingMessages : UIServiceBase<AppUIHub>, IComputeService, IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly Dictionary<ChatId, ChatSendingMessages> _chatSendingMessages = new ();
    private readonly WeakValueTable<string, ChatEntry> _clientEntries = new ();
    private readonly List<(SendingMessage, AttachmentUploads)> _uploads = new ();
    private readonly Lock _chatSendingMessagesLock = new (); // This lock is used add/remove ChatSendingMessages and add/remove items inside.
    private readonly PostRequestsStorage _requestsStorage;
    private readonly Task _whenReady;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly MessageProcessor<PostMessageQueueItem> _messageProcessor;
    // ReSharper disable once NotAccessedField.Local
    private readonly Task _pruneSendingMessagesTask;

    private AnalyticEvents AnalyticEvents => Hub.AnalyticEvents;
    private Moment Now => Hub.Clocks.SystemClock.Now;

    public SendingMessages(AppUIHub hub) : base(hub)
    {
        DebugLog?.LogInformation("SendingMessages constructor");
        _requestsStorage = new PostRequestsStorage(hub);
        _whenReady = BackgroundTask.Run(StartStoredPostRequests);
        var cancellationToken = hub.BlazorAppLifecycle.StopToken;
        _cancellationTokenSource = cancellationToken.CreateLinkedTokenSource();
        _messageProcessor = new MessageProcessor<PostMessageQueueItem>(ProcessQueueItem, _cancellationTokenSource) {
            QueueSize = 100,
            QueueFullMode = BoundedChannelFullMode.Wait,
            ProcessCallTimeout = TimeSpan.Zero, // No limit to command processing
        };
        _pruneSendingMessagesTask = AsyncChain.From(PruneSendingMessages)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 3), Log)
            .AppendDelay(Interval)
            .CycleForever()
            .RunIsolated(cancellationToken);
    }

    public ChatSendingMessagesAccessor GetSendingMessages(ChatId chatId)
    {
        ChatSendingMessages chatSendingMessages;
        lock (_chatSendingMessagesLock)
            chatSendingMessages = GetChatSendingMessages(chatId);
        DebugLog?.LogInformation("-> GetSendingMessages. ChatId='{ChatId}'", chatId);
        return new ChatSendingMessagesAccessor(chatSendingMessages);
    }

    public async Task<Task<ChatEntry>> Post(PostMessageRequest cmd, CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("Post '{Text}'", cmd.Text);
        var now = Clocks.SystemClock.Now;
        var resultSource = TaskCompletionSourceExt.New<ChatEntry>();
        await _whenReady.ConfigureAwait(false);
        var uuid = Ulid.NewUlid().ToString();
        var entry = new PostRequestEntry(uuid, cmd, now);
        DebugLog?.LogInformation("About to store post request '{Text}'", cmd.Text);
        await _requestsStorage.Add(entry, cancellationToken).ConfigureAwait(false);
        _ = BackgroundTask.Run(async () => {
            DebugLog?.LogInformation("About to post internal '{Text}'", cmd.Text);
            await PostInternal(entry, resultSource, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
        return resultSource.Task;
    }

    public void Cancel(SendingMessage sendingMessage)
        => sendingMessage.Cancel();

    private async Task StartStoredPostRequests()
    {
        DebugLog?.LogInformation("StartStoredPostRequests");
        CancellationToken cancellationToken = CancellationToken.None;
        var entries = await _requestsStorage.GetStored(cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries) {
            var resultSource = TaskCompletionSourceExt.New<ChatEntry>();
            _ = PostInternal(entry, resultSource, cancellationToken);
            _ = BackgroundTask.Run(async () => {
                try {
                    _ = await resultSource.Task.ConfigureAwait(false);
                }
                catch (Exception e) {
                    Log.LogError(e, "Failed to post stored post request");
                }
            }, cancellationToken);
        }
    }

    private async Task PostInternal(PostRequestEntry entry, TaskCompletionSource<ChatEntry> resultSource, CancellationToken cancellationToken)
    {
        try {
            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cancellationToken1 = cancellationTokenSource.Token;
            var queueMessageProcess = _messageProcessor.Enqueue(new PostMessageQueueItem(entry.Request), cancellationToken1);
            var sendingMessage = CreateSendingMessage(entry, cancellationTokenSource);
            ChatSendingMessages chatSendingMessages;
            lock (_chatSendingMessagesLock) {
                chatSendingMessages = GetChatSendingMessages(entry.Request.ChatId);
                chatSendingMessages.AddSendingMessage(sendingMessage);
                if (entry.Request.AttachmentUploads is not null)
                    _uploads.Add((sendingMessage, entry.Request.AttachmentUploads));
            }
            DebugLog?.LogInformation("Sending message: LocalId={LocalId}, Content='{Content}'",
                entry.Request.LocalId,
                entry.Request.Text);
            Result<ChatEntry> result;
            try {
                // NOTE: wait on the cancellation token to fail fast on send message request cancellation
                // (not await when the command will be processed by queue processor)
                var postResult = await queueMessageProcess.WhenCompleted.WaitAsync(cancellationToken1).ConfigureAwait(false);
                var chatEntry = (ChatEntry)postResult!;
                result = new Result<ChatEntry>(chatEntry);
                DebugLog?.LogInformation("Sent message: LocalId={LocalId}, Content='{Content}'",
                    chatEntry.LocalId,
                    chatEntry.Content);
            }
            catch (Exception e) {
                // NOTE(DF): react on critical errors like have no longer permissions to send a message.
                // Then we should abundant this request and inform the user.
                Log.LogError(e, "Failed to sent message. UUID={Uuid}, Text='{Text}'", entry.Uuid, entry.Request.Text);
                result = Result.NewError<ChatEntry>(e);
            }
            try {
                // TODO: decide when we can cancel remove request.
                CancellationToken cancellationToken2 = default;
                await _requestsStorage.Remove(entry.Uuid, cancellationToken2).SilentAwait(false);
            }
            catch (Exception e) {
                Log.LogError(e,
                    "Failed to remove stored request to sent message. UUID={Uuid}, Text='{Text}'",
                    entry.Uuid,
                    entry.Request.Text);
            }
            if (result.IsValue(out var chatEntry1, out var exception)) {
                chatSendingMessages.ConfirmMessageHasSent(sendingMessage, chatEntry1, Now);
                if (entry.Request.AttachmentUploads is not null)
                    await CreateAttachments(chatEntry1, entry.Request.AttachmentUploads, default).ConfigureAwait(false);
                resultSource.SetResult(chatEntry1);
            }
            else if (cancellationToken1.IsCancellationRequested) {
                chatSendingMessages.ConfirmMessageFailedToSend(sendingMessage, exception);
                resultSource.TrySetCanceled();
            }
            else {
                chatSendingMessages.ConfirmMessageFailedToSend(sendingMessage, exception);
                resultSource.TrySetException(exception);
            }
        }
        catch (Exception e) {
            Log.LogError(e,
                "Failed to sent message. UUID={Uuid}, Text='{Text}'",
                entry.Uuid,
                entry.Request.Text);
        }
    }

    private static SendingMessage CreateSendingMessage(
        PostRequestEntry entry,
        CancellationTokenSource cancellationTokenSource)
    {
        var cmd = entry.Request;
        var isNewMessage = cmd.LocalId is null;
        // NOTE(DF): we need to set content hash to trigger ChatEntryMessageInternalView re-rendering for edited messages.
        var textHash = isNewMessage ? HashString.None : cmd.Text.Hash().Blake2b().ToBase64HashString(HashAlgorithm.Blake2b);
        var sendingMessage = new SendingMessage(cmd.ChatId,
            cmd.LocalId,
            entry.Now,
            cmd.Text,
            textHash,
            entry.Uuid,
            cmd.AttachmentUploads,
            cancellationTokenSource);
        return sendingMessage;
    }

    private async Task<object?> ProcessQueueItem(PostMessageQueueItem command, CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("-> ProcessQueueItem. Text: '{Text}'", command.Request.Text);
        ChatEntry chatEntry = await ProcessCommand(command, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogInformation("<- ProcessQueueItem. Text: '{Text}'", command.Request.Text);
        return chatEntry;
    }

    private async Task<ChatEntry> ProcessCommand(PostMessageQueueItem command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var cmd = new Chats_UpsertTextEntry(Session, request.ChatId, request.LocalId, request.Text, request.RepliedEntryLid) {
            HasAttachmentUploads = request.AttachmentUploads is not null && request.AttachmentUploads.Attachments.Count > 0,
        };
        // Simulate long sending
        await Task.Delay(8000, cancellationToken).ConfigureAwait(false);
        var postResult = await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);
        var chatEntry = postResult.Result.Value;
        var isNewMessage = cmd.LocalId is null;
        if (isNewMessage)
            AnalyticEvents.RaiseMessagePosted(
                cmd.RepliedEntryLid.HasValue,
                !cmd.Text.IsNullOrEmpty(),
                cmd.EntryAttachments.Length);
        return chatEntry;
    }

    private async Task CreateAttachments(ChatEntry chatEntry, AttachmentUploads mediaUploads, CancellationToken cancellationToken = default)
    {
        // An attachment should have several states: loading, loading error, canceled (removed), loaded.
        // When an attachment has entered the `loading error` state, you can repeat the loading or cancel it entirely.
        await mediaUploads.WhenUploaded.ConfigureAwait(false);
        // When all attachments are loaded, we can continue execution.
        var entryAttachments = mediaUploads.Attachments.Items
            .Where(x => x.Uploaded)
            .Select(x => new TextEntryAttachment {
                MediaId = x.MediaId!,
                ThumbnailMediaId = x.ThumbnailMediaId,
            }).ToArray();
        if (entryAttachments.Length == 0 && chatEntry.Content.IsNullOrEmpty()) {
            // If there are no loaded attachments and the ChatEntry Content is empty,
            // then we delete this ChatEntry altogether.
            var cmd = new Chats_RemoveTextEntry(Session, chatEntry.ChatId, chatEntry.LocalId);
            await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);
        }
        else {
            // If there are loaded attachments,
            // then we add them to the ChatEntry and mark that there are no more loadings.
            // NOTE(DF): may be better to introduce a new command for this.
            var cmd = new Chats_UpsertTextEntry(Session, chatEntry.ChatId, chatEntry.LocalId, chatEntry.Content) {
                HasAttachmentUploads = false,
                EntryAttachments = entryAttachments,
            };
            await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);
        }
        await mediaUploads.Attachments.DisposeSilentlyAsync().ConfigureAwait(false);
    }

    private ChatSendingMessages GetChatSendingMessages(ChatId chatId)
    {
        if (_chatSendingMessages.TryGetValue(chatId, out var chatSendingMessages))
            return chatSendingMessages;

        chatSendingMessages = new ChatSendingMessages(this, chatId);
        _chatSendingMessages.Add(chatId, chatSendingMessages);
        return chatSendingMessages;
    }

    private Task PruneSendingMessages(CancellationToken cancellationToken)
    {
        lock (_chatSendingMessagesLock) {
            var keys = _chatSendingMessages.Keys.ToArray();
            foreach (var chatId in keys) {
                if (!_chatSendingMessages.TryGetValue(chatId, out var chatSendingMessages))
                    continue;

                chatSendingMessages.PruneSentMessages(Now);
                // NOTE(DF): do not remove empty, they will be recreated on GetSendingMessages again.
                // if (chatSendingMessages.IsEmpty) {
                //     _chatSendingMessages.Remove(chatId);
                //     DebugLog?.LogInformation("Removed ChatSendingMessages for chat '{ChatId}'", chatId);
                // }
            }
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _messageProcessor.Complete(CancellationToken.None).SilentAwait(false);
        await _messageProcessor.DisposeAsync().ConfigureAwait(false);
        _cancellationTokenSource.Dispose();
    }

    public void RegisterEntryByClientId(ChatEntry chatEntry)
    {
        lock (_clientEntries.SyncObject)
            _clientEntries.AddOrUpdate(chatEntry.ClientUid, chatEntry);
    }

    public ChatEntry? TryGetChatEntryByClientId(string clientId)
    {
        lock (_clientEntries.SyncObject)
            return _clientEntries.TryGetValue(clientId, out var chatEntry) ? chatEntry : null;
    }

    // Nested types

    public record PostMessageQueueItem(PostMessageRequest Request);

    public AttachmentUploads? GetMediaUploads(ChatEntry entry)
    {
        lock (_chatSendingMessagesLock) {
            if (entry.IsSending) {
                var uploadsItem = _uploads.FirstOrDefault(c => Equals(c.Item1, entry.SendingTag));
                return uploadsItem.Item2;
            }
            else {
                var uploadsItem = _uploads.FirstOrDefault(c => Equals(c.Item1.PostedChatEntry?.Id, entry.Id));
                return uploadsItem.Item2;
            }
        }
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record PostMessageRequest(
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] long? LocalId,
    [property: DataMember, MemoryPackOrder(3)] string Text,
    [property: DataMember, MemoryPackOrder(4)] Option<long?> RepliedEntryLid = default
)
{
    // [DataMember, MemoryPackOrder(11)] public TextEntryAttachment[] EntryAttachments { get; set; } = [];
    //[DataMember, MemoryPackOrder(12)]
    [IgnoreDataMember, MemoryPackIgnore]
    public AttachmentUploads? AttachmentUploads { get; init; }
}

public sealed class AttachmentUploads
{
    private readonly TaskCompletionSource _whenUploaded = TaskCompletionSourceExt.New();

    public IAttachmentList Attachments { get; }
    public Task WhenUploaded => _whenUploaded.Task;

    public static AttachmentUploads? From(IAttachmentList attachments)
        => attachments.Count > 0 ? new AttachmentUploads(attachments) : null;

    public AttachmentUploads(IAttachmentList attachments)
    {
        if (attachments.Count is 0)
            throw new ArgumentException("Attachments must not be empty.", nameof(attachments));

        Attachments = attachments;
        attachments.Changed += (_, _) => ReviewState();
        ReviewState();
    }

    private void ReviewState()
    {
        var isCompleted = Attachments.Items.All(c => c.Uploaded);
        if (isCompleted)
            _whenUploaded.TrySetResult();
    }
}
