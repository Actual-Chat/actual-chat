using ActualChat.Hashing;
using ActualChat.Messaging;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class SendingMessages : UIServiceBase<AppUIHub>, IComputeService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<ChatId, ChatSendingMessages> _chatSendingMessages = new ();
    private readonly PostRequestsStorage _requestsStorage;
    private readonly Task _whenReady;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly MessageProcessor<PostMessageQueueItem> _messageProcessor;

    private AnalyticEvents AnalyticEvents => Hub.AnalyticEvents;

    public SendingMessages(AppUIHub hub) : base(hub)
    {
        _requestsStorage = new PostRequestsStorage(hub);
        _whenReady = BackgroundTask.Run(StartStoredPostRequests);
        _cancellationTokenSource = hub.BlazorAppLifecycle.StopToken.CreateLinkedTokenSource();
        _messageProcessor = new MessageProcessor<PostMessageQueueItem>(ProcessQueueItem, _cancellationTokenSource) {
            QueueSize = 100,
            QueueFullMode = BoundedChannelFullMode.Wait,
            ProcessCallTimeout = TimeSpan.Zero, // No limit to command processing
        };
    }

    public ChatSendingMessagesAccessor GetSendingMessages(ChatId chatId, long rangeEnd)
    {
        var chatSendingMessages = GetChatSendingMessages(chatId);
        DebugLog?.LogInformation("-> GetSendingMessages. ChatId='{ChatId}', RangeEnd='{RangeEnd}'", chatId, rangeEnd);
        return new ChatSendingMessagesAccessor(chatSendingMessages, rangeEnd);
    }

    public async Task<Task<ChatEntry>> Post(Chats_UpsertTextEntry cmd, CancellationToken cancellationToken)
    {
        var now = Clocks.SystemClock.Now;
        var resultSource = TaskCompletionSourceExt.New<ChatEntry>();
        await _whenReady.ConfigureAwait(false);
        var uuid = Ulid.NewUlid().ToString();
        var entry = new PostRequestEntry(uuid, cmd, now);
        await _requestsStorage.Add(entry, cancellationToken).ConfigureAwait(false);
        _ = BackgroundTask.Run(async () => {
            await PostInternal(entry, resultSource, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
        return resultSource.Task;
    }

    private async Task StartStoredPostRequests()
    {
        CancellationToken cancellationToken = CancellationToken.None;
        var entries = await _requestsStorage.GetStored(cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries) {
            var entry1 = entry with {
                Command = entry.Command with {
                    Session = Session,
                },
            };
            var resultSource = TaskCompletionSourceExt.New<ChatEntry>();
            _ = PostInternal(entry1, resultSource, cancellationToken);
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
            var queueMessageProcess = _messageProcessor.Enqueue(new PostMessageQueueItem(entry.Command), cancellationToken);
            var chatSendingMessages = GetChatSendingMessages(entry.Command.ChatId);
            var sendingMessage = CreateSendingMessage(entry);
            chatSendingMessages.AddSendingMessage(sendingMessage);
            DebugLog?.LogInformation("Sending message: LocalId={LocalId}, Content='{Content}'",
                entry.Command.LocalId,
                entry.Command.Text);
            Result<ChatEntry> result;
            try {
                var postResult = await queueMessageProcess.WhenCompleted.ConfigureAwait(false);
                var chatEntry = (ChatEntry)postResult!;
                result = new Result<ChatEntry>(chatEntry);
                DebugLog?.LogInformation("Sent message: LocalId={LocalId}, Content='{Content}'",
                    chatEntry.LocalId,
                    chatEntry.Content);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to sent message. UUID={Uuid}, Text='{Text}'", entry.Uuid, entry.Command.Text);
                result = Result.NewError<ChatEntry>(e);
            }
            try {
                await _requestsStorage.Remove(entry.Uuid, cancellationToken).SilentAwait(false);
            }
            catch (Exception e) {
                Log.LogError(e,
                    "Failed to remove stored request to sent message. UUID={Uuid}, Text='{Text}'",
                    entry.Uuid,
                    entry.Command.Text);
            }
            if (result.IsValue(out var chatEntry1, out var exception)) {
                chatSendingMessages.ConfirmMessageHasSent(sendingMessage, chatEntry1);
                resultSource.SetResult(chatEntry1);
            }
            else {
                chatSendingMessages.ConfirmMessageFailedToSend(sendingMessage);
                resultSource.TrySetException(exception);
            }
        }
        catch (Exception e) {
            Log.LogError(e,
                "Failed to sent message. UUID={Uuid}, Text='{Text}'",
                entry.Uuid,
                entry.Command.Text);
        }
    }

    private static SendingMessage CreateSendingMessage(PostRequestEntry entry)
    {
        var cmd = entry.Command;
        var isNewMessage = cmd.LocalId is null;
        // NOTE(DF): we need to set content hash to trigger ChatEntryMessageInternalView re-rendering for edited messages.
        var textHash = isNewMessage ? HashString.None : cmd.Text.Hash().Blake2b().ToBase64HashString(HashAlgorithm.Blake2b);
        var sendingMessage = new SendingMessage(cmd.ChatId, cmd.LocalId, entry.Now, cmd.Text, textHash);
        return sendingMessage;
    }

    private async Task<object?> ProcessQueueItem(PostMessageQueueItem command, CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("-> ProcessQueueItem. Text: '{Text}'", command.Command.Text);
        ChatEntry chatEntry = await ProcessCommand(command, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogInformation("<- ProcessQueueItem. Text: '{Text}'", command.Command.Text);
        return chatEntry;
    }

    private async Task<ChatEntry> ProcessCommand(PostMessageQueueItem command, CancellationToken cancellationToken)
    {
        var cmd = command.Command;
        // Simulate long sending
        await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
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

    private ChatSendingMessages GetChatSendingMessages(ChatId chatId)
        => _chatSendingMessages.GetOrAdd(chatId, static (chatId1, self) => new ChatSendingMessages(self, chatId1), this);

    public async ValueTask DisposeAsync()
    {
        await _messageProcessor.Complete(CancellationToken.None).SilentAwait(false);
        await _messageProcessor.DisposeAsync().ConfigureAwait(false);
        _cancellationTokenSource.Dispose();
    }

    // Nested types

    public record PostMessageQueueItem(Chats_UpsertTextEntry Command);
}
