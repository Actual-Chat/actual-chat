using ActualChat.Hashing;
using ActualChat.Media;
using ActualChat.Messaging;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class SendingMessages : UIServiceBase<AppUIHub>, IComputeService, IAsyncDisposable
{
    public static class AfterSendMessageHandlerKeys
    {
        public const string IncomingShare = "IncomingShare";
    }

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly Dictionary<ChatId, ChatSendingMessages> _chatSendingMessages = new ();
    private readonly WeakValueTable<string, ChatEntry> _clientEntries = new ();
    private readonly MediaUploadsUI _mediaUploadsUI;
    private readonly Lock _chatSendingMessagesLock = new (); // This lock is used add/remove ChatSendingMessages and add/remove items inside.
    private readonly SendMessageRequestsRepo _requestsRepo;
    private readonly Task _whenReady;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly MessageProcessor<PostMessageQueueItem> _messageProcessor;
    // ReSharper disable once NotAccessedField.Local
    private readonly Task _pruneSendingMessagesTask;
    private readonly ChatSendingMessagesTriggers _triggers;

    private AnalyticEvents AnalyticEvents => Hub.AnalyticEvents;
    private UploadSessions UploadSessions => Hub.UploadSessions;
    private IChats Chats => Hub.Chats;
    private Moment Now => Clocks.SystemClock.Now;

    public Task WhenReady => _whenReady;

    public SendingMessages(AppUIHub hub) : base(hub)
    {
        DebugLog?.LogInformation("SendingMessages constructor");
        _requestsRepo = new SendMessageRequestsRepo(hub);
        _triggers = Services.GetRequiredService<ChatSendingMessagesTriggers>();
        _mediaUploadsUI = new MediaUploadsUI(_triggers);
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

    public void NotifyAttachmentsLoaded(ChatEntryId chatEntryId)
        => _mediaUploadsUI.NotifyAttachmentsLoaded(chatEntryId);

    public AttachmentUploads? GetMediaUploads(ChatEntry entry)
        => _mediaUploadsUI.GetMediaUploads(entry);

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

    public void Cancel(SendingMessage sendingMessage)
        => sendingMessage.Cancel();

    public async Task<Task<ChatEntry?>> Post(SendMessageRequest cmd, CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("Post '{Text}'", cmd.Text);
        var now = Clocks.SystemClock.Now;
        var resultSource = TaskCompletionSourceExt.New<ChatEntry?>();
        await _whenReady.ConfigureAwait(false);
        var uuid = Ulid.NewUlid().ToString();
        var attachEntries = new List<AttachFileRequestEntry>();
        if (cmd.Attachments is not null) {
            foreach (var attachment in cmd.Attachments.Items) {
                var uploadSessionId = attachment.UploadSessionId;
                if (uploadSessionId.IsNullOrEmpty()) {
                    if (attachment.FileProvider is not { } fileProvider)
                        throw new InvalidOperationException($"Can't initialize upload for attachment '{attachment.Id}'. No file provider assigned.");

                    var uploadSession = await UploadSessions.CreateSession(cmd.ChatId, fileProvider).ConfigureAwait(false);
                    uploadSessionId = uploadSession.SessionId;
                }
                var attachEntry = new AttachFileRequestEntry(uploadSessionId, attachment.FileName, attachment.FileType, attachment.Length, attachment.Width, attachment.Height);
                attachEntries.Add(attachEntry);
            }
        }
        var clientId = !cmd.LocalId.HasValue ? Guid.NewGuid().ToString() : "";
        var entry = new SendMessageRequestEntry(
            uuid, now,
            cmd.ChatId, cmd.LocalId, cmd.Text, cmd.RepliedEntryLid,
            attachEntries.ToArray(), clientId,
            cmd.AfterSendMessageHandlerKey, cmd.AfterSendMessageHandlerArgs);
        DebugLog?.LogInformation("About to store post request '{Text}'", cmd.Text);
        await _requestsRepo.Add(entry, cancellationToken).ConfigureAwait(false);

        var uploads = await CreateAttachmentUploads(entry, cmd.Attachments).ConfigureAwait(false);
        var requestInternal = CreatePostMessageRequestInternal(entry, uploads, false);
        _ = BackgroundTask.Run(async () => {
            DebugLog?.LogInformation("About to post internal '{Text}'", cmd.Text);
            await PostInternal(requestInternal, resultSource, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
        return resultSource.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await _messageProcessor.DisposeAsync().ConfigureAwait(false);
        _cancellationTokenSource.Dispose();
    }

    private static PostMessageRequestInternal CreatePostMessageRequestInternal(SendMessageRequestEntry entry,
        AttachmentUploads? uploads, bool checkResend)
        => new (entry.Uuid,
            entry.Now,
            entry.ChatId,
            entry.LocalId,
            entry.Text,
            entry.RepliedEntryLid,
            uploads,
            entry.ClientId,
            entry.NewChatEntryLocalId,
            entry.AfterSendMessageHandlerKey,
            entry.AfterSendMessageHandlerArgs,
            checkResend);

    private async Task<AttachmentUploads?> CreateAttachmentUploads(SendMessageRequestEntry entry, IAttachmentList? sourceAttachments)
    {
        var attachEntries = entry.AttachFileRequests;
        if (attachEntries.Length == 0)
            return null;

        var sourceAttachmentsCopy = sourceAttachments?.Items.ToArray();
        if (sourceAttachmentsCopy is not null && sourceAttachmentsCopy.Length != attachEntries.Length)
            sourceAttachmentsCopy = null;

        var attachments = new List<Attachment>();
        for (var i = 0; i < attachEntries.Length; i++) {
            var attachEntry = attachEntries[i];
            var sourceAttachment = sourceAttachmentsCopy?[i];
            var previewUrl = sourceAttachment?.PreviewUrl ?? "";
            var uploadSessionId = attachEntry.UploadSessionId;
            // TODO: to think what to do with this. For now UploadApp is responsible for resuming stored sessions.
            var attachmentIsOk = false;
            try {
                var session = await UploadSessions.TryGetSession(uploadSessionId).ConfigureAwait(false);
                if (session is not null) {
                    var fileProvider = session.FileProvider;
                    var canAccess = await fileProvider.CheckAccess().ConfigureAwait(false);
                    if (canAccess) {
                        if (previewUrl.IsNullOrEmpty() && MediaTypeExt.IsVisualMedia(session.FileProvider.Metadata.FileType))
                            previewUrl = await fileProvider.GetPreviewUrl().ConfigureAwait(false);
                        attachmentIsOk = true;
                    }
                }
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to get upload session '{UploadSessionId}'", uploadSessionId);
                // Intended
            }

            async Task CleanupRequest() {
                await _requestsRepo.RemoveAttachRequest(entry.Uuid, attachEntry, CancellationToken.None).ConfigureAwait(false);
            }

            var attachment = new Attachment(attachEntry.FileName, attachEntry.FileType, attachEntry.FileLength, previewUrl, attachEntry.Width, attachEntry.Height) {
                UploadSessionId = uploadSessionId,
            };
            attachment.Cleanups.Add(AttachmentCleanupFactory.ForUploadSession(UploadSessions, uploadSessionId));
            attachment.Cleanups.Add(new AttachmentCleanup(AttachmentCleanupKind.PersistedPostMessageRequest, CleanupRequest));
            if (!attachmentIsOk) {
                attachment = attachment with {
                    Failed = true,
                    NoAccess = true,
                };
            }
            attachments.Add(attachment);
        }
        if (attachments.Count == 0)
            return null;

        var attachmentsController = Services.GetRequiredService<AttachmentsController>();
        var attachmentList = new AttachmentList();
        foreach (var attachment in attachments) {
            await attachmentsController.AddAttachment(attachmentList, attachment).ConfigureAwait(false);
            if (!attachment.Failed)
                await attachmentsController.ResumeUpload(attachmentList, attachment.Id).ConfigureAwait(false);
        }
        return new AttachmentUploads(attachmentList);
    }

    private async Task StartStoredPostRequests()
    {
        DebugLog?.LogInformation("StartStoredPostRequests");
        CancellationToken cancellationToken = CancellationToken.None;
        var entries = await _requestsRepo.GetStored(cancellationToken).ConfigureAwait(false);
        var chatIds = new HashSet<ChatId>();
        foreach (var (uuid, entry) in entries) {
            if (entry is null) {
                // Failed to recreate a stored send request, let's forget about it.
                await DiscardStoredPostRequest(uuid, cancellationToken).ConfigureAwait(false);
                continue;
            }
            PostMessageRequestInternal requestInternal;
            try {
                var uploads = await CreateAttachmentUploads(entry, null).ConfigureAwait(false);
                var checkResend = chatIds.Add(entry.ChatId) && !entry.ClientId.IsNullOrEmpty(); // NOTE: check only for the first request per chat.
                requestInternal = CreatePostMessageRequestInternal(entry, uploads, checkResend);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to recreate stored post request");
                // Failed to restart send message request.
                // So forget about this request and cleanup attachment resources.
                await DiscardStoredPostRequest(entry.Uuid, cancellationToken).ConfigureAwait(false);
                var uploadSessionIds = entry.AttachFileRequests.Select(x => x.UploadSessionId).ToArray();
                await CleanupUploadSessions(entry.Uuid, uploadSessionIds).ConfigureAwait(false);
                continue;
            }
            var resultSource = TaskCompletionSourceExt.New<ChatEntry?>();
            _ = PostInternal(requestInternal, resultSource, cancellationToken);
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

    private async Task CleanupAttachments(string postRequestUuid, IEnumerable<Attachment> attachments)
    {
        foreach (var attachment in attachments) {
            try {
                var cleanups = attachment.Cleanups.Items.ToList();
                // NOTE(DF): we don't need to do individual cleanups per attachment for the persisted send message request.
                // Because it is already executed for the entire send message request.
                cleanups.RemoveAll(x => x.Kind == AttachmentCleanupKind.PersistedPostMessageRequest);
                foreach (var cleanup in cleanups)
                    await cleanup.Cleanup().ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to cleanup upload session '{UploadSessionId}' for post request '{Id}'",
                    attachment.UploadSessionId, postRequestUuid);
            }
        }
    }

    private async Task CleanupUploadSessions(string postRequestUuid, string[] uploadSessionIds)
    {
        foreach (var uploadSessionId in uploadSessionIds) {
            try {
                var cleanup = AttachmentCleanupFactory.ForUploadSession(UploadSessions, uploadSessionId);
                await cleanup.Cleanup().ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to cleanup upload session '{UploadSessionId}' for post request '{Id}'",
                    uploadSessionId, postRequestUuid);
            }
        }
    }

    private async Task DiscardStoredPostRequest(string postRequestUuid, CancellationToken cancellationToken)
    {
        try {
            await _requestsRepo.Remove(postRequestUuid, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to remove stored post request '{Id}'", postRequestUuid);
        }
    }

    private async Task PostInternal(PostMessageRequestInternal request, TaskCompletionSource<ChatEntry?> resultSource, CancellationToken cancellationToken)
    {
        try {
            DebugLog?.LogInformation("Sending message: LocalId={LocalId}, Content='{Content}', ClientId='{ClientId}', NewChatEntryLocalId={NewChatEntryLocalId}",
                request.LocalId, request.Text, request.ClientId, request.NewChatEntryLocalId);
            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cancellationToken1 = cancellationTokenSource.Token;
            Result<ChatEntry> result;
            ChatSendingMessages chatSendingMessages;
            var sendingMessage = CreateSendingMessage(request, cancellationTokenSource);
            lock (_chatSendingMessagesLock) {
                chatSendingMessages = GetChatSendingMessages(request.ChatId);
                chatSendingMessages.AddSendingMessage(sendingMessage);
                if (request.AttachmentUploads is not null)
                    _mediaUploadsUI.Add(sendingMessage, request.AttachmentUploads);
            }

            if (!request.NewChatEntryLocalId.HasValue) {
                var queueMessageProcess = _messageProcessor.Enqueue(new PostMessageQueueItem(request), cancellationToken1);
                try {
                    // NOTE: wait on the cancellation token to fail fast on send message request cancellation
                    // (not await when the command will be processed by queue processor)
                    var postResult = await queueMessageProcess.WhenCompleted.WaitAsync(cancellationToken1)
                        .ConfigureAwait(false);
                    var chatEntry = (ChatEntry)postResult!;
                    result = new Result<ChatEntry>(chatEntry);
                    DebugLog?.LogInformation("Sent message: LocalId={LocalId}, Content='{Content}'",
                        chatEntry.LocalId,
                        chatEntry.Content);
                }
                catch (Exception e) {
                    // NOTE(DF): react on critical errors like have no longer have permissions to send a message to the chat.
                    // Then we should discard this request and inform the user.
                    Log.LogError(e, "Failed to sent message. UUID={Uuid}, Text='{Text}'", request.Uuid, request.Text);
                    result = Result.NewError<ChatEntry>(e);
                }
            }
            else {
                var chatEntryId = ChatEntryId.New(request.ChatId, ChatEntryKind.Text, request.NewChatEntryLocalId.Value);
                var chatEntry = await Hub.Chats.GetEntry(Hub.Session, chatEntryId, cancellationToken1).ConfigureAwait(false);
                if (chatEntry is not null)
                    result = Result.New(chatEntry);
                else
                    result = Result.NewError<ChatEntry>(StandardError.Internal($"Can't find chat entry with id '{chatEntryId}'."));
            }

            if (result.IsValue(out var chatEntry1, out var exception)) {
                chatSendingMessages.ConfirmMessageHasSent(sendingMessage, chatEntry1, Now);
                _mediaUploadsUI.Invalidate(sendingMessage);
                if (chatEntry1.HasAttachmentUploads && request.AttachmentUploads is not null) {
                    if (!request.NewChatEntryLocalId.HasValue)
                        await _requestsRepo.MarkMessageHasCreated(request.Uuid, chatEntry1.LocalId, cancellationToken1).ConfigureAwait(false);
                    var chatEntry2 = await CreateAttachments(chatEntry1, request.AttachmentUploads, default).ConfigureAwait(false);
                    // Delete upload info with delay to avoid flickering in the UI.
                    chatEntry1 = chatEntry2;
                    _ = BackgroundTask.Run(async () => {
                        await Task.Delay(TimeSpan.FromMinutes(1), CancellationToken.None).ConfigureAwait(false);
                        _mediaUploadsUI.Delete(sendingMessage);
                    }, CancellationToken.None);
                }
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
                request.Uuid,
                request.Text);
            resultSource.TrySetException(e);
        }

        // Everything is completed. We can forget about this request and cleanup attachment resources.
        await DiscardStoredPostRequest(request.Uuid, cancellationToken).ConfigureAwait(false);
        if (request.AttachmentUploads is not null)
            await CleanupAttachments(request.Uuid, request.AttachmentUploads.Attachments.Items).ConfigureAwait(false);

        var task = resultSource.Task;
        Result<ChatEntry?> result2 =
            task.IsCompletedSuccessfully ? new Result<ChatEntry?>(task.Result)
            : task.IsCanceled ? Result.NewError<ChatEntry?>(new OperationCanceledException())
            : Result.NewError<ChatEntry?>(task.Exception!);

        InvokeAfterSendMessageHandler(
            request.AfterSendMessageHandlerKey,
            request.AfterSendMessageHandlerArgs,
            result2);
    }

    private void InvokeAfterSendMessageHandler(string afterSendMessageHandlerKey, string afterSendMessageHandlerArgs, Result<ChatEntry?> result)
    {
        if (afterSendMessageHandlerKey.IsNullOrEmpty())
            return;

        IAfterSendMessageHandler handler = afterSendMessageHandlerKey switch {
            AfterSendMessageHandlerKeys.IncomingShare => Hub.Services.GetRequiredService<IncomingShareAfterSendMessageHandler>(),
            _ => throw new InvalidOperationException($"Unknown after send message handler key '{afterSendMessageHandlerKey}'")
        };

        handler.Invoke(afterSendMessageHandlerArgs, result);
    }

    private static SendingMessage CreateSendingMessage(
        PostMessageRequestInternal request,
        CancellationTokenSource cancellationTokenSource)
    {
        var isNewMessage = request.LocalId is null;
        // NOTE(DF): we need to set the content hash to trigger ChatEntryMessageInternalView re-rendering for edited messages.
        var textHash = isNewMessage ? HashString.None : request.Text.Hash().Blake2b().ToBase64HashString(HashAlgorithm.Blake2b);
        var sendingMessage = new SendingMessage(request.Uuid,
            request.ChatId,
            request.LocalId,
            request.Now,
            request.Text,
            textHash,
            request.AttachmentUploads,
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
        if (request.CheckResend) {
            var chatEntry1 = await TryFindPreviouslySendEntry(request.ChatId, request.ClientId, cancellationToken).ConfigureAwait(false);
            if (chatEntry1 is not null)
                return chatEntry1;
        }
        var cmd = new Chats_UpsertTextEntry(Session, request.ChatId, request.LocalId, request.Text, request.RepliedEntryLid) {
            ClientId = request.ClientId,
        };
        if (request.AttachmentUploads is not null) {
            if (!request.AttachmentUploads.WhenUploaded.IsCompletedSuccessfully)
                cmd = cmd with { HasAttachmentUploads = true };
            else
                cmd = cmd with { EntryAttachments = CreateTextEntryAttachments(request.AttachmentUploads) };
        }
        // // Simulate long sending
        // await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
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

    private async Task<ChatEntry?> TryFindPreviouslySendEntry(ChatId chatId, string clientId, CancellationToken cancellationToken)
    {
        var range = await Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken).ConfigureAwait(false);
        if (range.IsEmpty)
            return null;

        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return null;

        var ownAuthor = chat.Rules.Author;
        if (ownAuthor is null)
            return null;

        var ownAuthorId = ownAuthor.Id;
        var entryReader = Chats.NewEntryReader(Session, chatId, ChatEntryKind.Text);
        var counter = 0;
        const int maxResendScanCount = 200; // Scan the last 200 messages
        await foreach (var chatEntry1 in entryReader.ReadReverse(range, cancellationToken).ConfigureAwait(false)) {
            if (chatEntry1.AuthorId == ownAuthorId && OrdinalEquals(chatEntry1.ClientId, clientId))
                return chatEntry1;

            counter++;
            if (counter >= maxResendScanCount)
                break;
        }
        return null;
    }

    private async Task<ChatEntry?> CreateAttachments(ChatEntry chatEntry, AttachmentUploads attachmentUploads, CancellationToken cancellationToken = default)
    {
        // An attachment should have several states: loading, loading error, canceled (removed), loaded.
        // When an attachment has entered the `loading error` state, you can repeat the loading or cancel it entirely.
        await attachmentUploads.WhenUploaded.ConfigureAwait(false);
        // When all attachments are loaded, we can continue execution.
        var entryAttachments = CreateTextEntryAttachments(attachmentUploads);
        if (entryAttachments.Length == 0 && chatEntry.Content.IsNullOrEmpty()) {
            // If there are no loaded attachments and the ChatEntry Content is empty,
            // then we delete this ChatEntry altogether.
            var cmd1 = new Chats_RemoveTextEntry(Session, chatEntry.ChatId, chatEntry.LocalId);
            await UICommander.Run(cmd1, cancellationToken).ConfigureAwait(false);
            return null;
        }
        // If there are loaded attachments,
        // then we add them to the ChatEntry and mark that there are no more loadings.
        // NOTE(DF): may be better to introduce a new command for this.
        var cmd = new Chats_UpsertTextEntry(Session, chatEntry.ChatId, chatEntry.LocalId, chatEntry.Content) {
            HasAttachmentUploads = false,
            EntryAttachments = entryAttachments,
        };
        return await UICommander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }

    private static TextEntryAttachment[] CreateTextEntryAttachments(AttachmentUploads attachmentUploads)
    {
        var entryAttachments = attachmentUploads.Attachments.Items
            .Where(x => x.Uploaded)
            .Select(x => new TextEntryAttachment {
                MediaId = x.MediaId!,
                ThumbnailMediaId = x.ThumbnailMediaId,
            }).ToArray();
        return entryAttachments;
    }

    private ChatSendingMessages GetChatSendingMessages(ChatId chatId)
    {
        if (_chatSendingMessages.TryGetValue(chatId, out var chatSendingMessages))
            return chatSendingMessages;

        chatSendingMessages = new ChatSendingMessages(this, _triggers, chatId);
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

    // Nested types

    public record PostMessageRequestInternal(string Uuid,
        Moment Now,
        ChatId ChatId,
        long? LocalId,
        string Text,
        Option<long?> RepliedEntryLid,
        AttachmentUploads? AttachmentUploads,
        string ClientId,
        long? NewChatEntryLocalId,
        string AfterSendMessageHandlerKey,
        string AfterSendMessageHandlerArgs,
        bool CheckResend
    ) : IHasId<string>
    {
        string IHasId<string>.Id => Uuid;
    }

    public record PostMessageQueueItem(PostMessageRequestInternal Request);
}
