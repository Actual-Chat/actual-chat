using ActualChat.Messaging;
using ActualChat.UI.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages message sending with persistent retry, attachment uploads, and resend detection.
/// </summary>
public partial class SendingMessages : UIServiceBase<AppUIHub>, IComputeService, IAsyncDisposable
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
    private readonly Task _whenStoredRequestsProcessed;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly MessageProcessor<PostMessageQueueItem> _messageProcessor;
    // ReSharper disable once NotAccessedField.Local
    private readonly Task _pruneSendingMessagesTask;
    private readonly ChatSendingMessagesTriggers _triggers;
    private readonly FilesUploadRegistry _filesUploadRegistry = new();

    private AnalyticEvents AnalyticEvents => Hub.AnalyticEvents;
    private UploadSessions UploadSessions => Hub.UploadSessions;
    private AttachmentsState AttachmentsState => Hub.AttachmentsState;
    private IChats Chats => Hub.Chats;
    private IncomingShareSuggestions? IncomingShareSuggestions { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public Task WhenStoredRequestsProcessed => _whenStoredRequestsProcessed;

    public SendingMessages(AppUIHub hub) : base(hub)
    {
        IncomingShareSuggestions = Services.GetService<IncomingShareSuggestions>();
        DebugLog?.LogDebug("SendingMessages constructor");
        _requestsRepo = new SendMessageRequestsRepo(hub);
        _triggers = Services.GetRequiredService<ChatSendingMessagesTriggers>();
        _mediaUploadsUI = new MediaUploadsUI(_triggers);
        var lifetimeToken = hub.BlazorAppLifecycle.StopToken;
        _cancellationTokenSource = lifetimeToken.CreateLinkedTokenSource();
        var serviceToken = _cancellationTokenSource.Token;
        _whenStoredRequestsProcessed = BackgroundTask.Run(StartStoredPostRequests, serviceToken);
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
            .RunIsolated(serviceToken);
    }

    public async Task<FilesUploadHandle?> Upload(ImmutableArray<Attachment> attachments, string mediaScope = "")
    {
        if (attachments.Length == 0)
            return null;

        var uploadEntries = new List<UploadFileRequestEntry>();
        foreach (var attachment in attachments) {
            var uploadSessionId = attachment.UploadSessionId;
            if (uploadSessionId.IsNullOrEmpty()) {
                if (attachment.FileProvider is not { } fileProvider)
                    throw new InvalidOperationException($"Can't initialize upload for attachment '{attachment.Id}'. No file provider assigned.");

                uploadSessionId = await UploadSessions.CreateSession(fileProvider, attachment.GetMetadataForUploadSession(), mediaScope).ConfigureAwait(false);
            }
            var attachEntry = new UploadFileRequestEntry(uploadSessionId, attachment.FileName, attachment.FileType, attachment.Length, attachment.Width, attachment.Height, attachment.Id);
            uploadEntries.Add(attachEntry);
        }

        var uploadSessionIds = uploadEntries.Select(c => c.UploadSessionId).ToImmutableArray();
        foreach (var uploadSessionId in uploadSessionIds)
            UploadSessions.AddReference(uploadSessionId);
        Func<Task> releaseUpload = () => {
            foreach (var uploadSessionId in uploadSessionIds)
                UploadSessions.ReleaseReference(uploadSessionId);
            return Task.CompletedTask;
        };

        var upload = new FilesUpload(attachments, uploadEntries.ToArray());
        return _filesUploadRegistry.Register(upload, releaseUpload);
    }

    public async Task<Task<ChatEntry?>> Send(SendMessageRequest cmd, CancellationToken cancellationToken)
    {
        // return FakeSend();
        DebugLog?.LogDebug("Post '{Text}'", cmd.Text.ToPrivate());
        var now = Clocks.SystemClock.Now;
        var resultSource = TaskCompletionSourceExt.New<ChatEntry?>();
        await _whenStoredRequestsProcessed.ConfigureAwait(false);
        var uuid = Ulid.NewUlid().ToString();
        var filesUpload = cmd.Uploads is not null ? _filesUploadRegistry.Get(cmd.Uploads) : null;
        var entry = CreateStoredSendRequest(uuid, now, cmd, filesUpload);
        DebugLog?.LogDebug("About to store post request '{Text}'", cmd.Text.ToPrivate());
        await _requestsRepo.Add(entry, cancellationToken).ConfigureAwait(false);

        var uploads = await CreateAttachmentUploads(entry, filesUpload?.Attachments).ConfigureAwait(false);
        var requestInternal = CreatePostMessageRequestInternal(entry, uploads, false);
        // Link the caller's CT with the service lifetime so this work is cancelled on dispose.
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
        _ = BackgroundTask.Run(async () => {
            try {
                DebugLog?.LogDebug("About to post internal '{Text}'", cmd.Text.ToPrivate());
                await PostInternal(requestInternal, resultSource, linkedCts.Token).ConfigureAwait(false);
            }
            finally {
                linkedCts.Dispose();
            }
        }, linkedCts.Token);
        return resultSource.Task;
    }

    public async ValueTask DisposeAsync()
    {
        // Cancel ASAP so all in-flight background tasks (Send, StartStoredRequests, prune chain)
        // unwind before we await the message processor.
        _cancellationTokenSource.CancelAndDisposeSilently();
        try {
            await _messageProcessor.DisposeAsync().AsTask()
                .WaitAsync(CoreConstants.DisposeTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning(
                "{Type}: message processor didn't dispose in {Timeout}, proceeding",
                GetType().GetName(), CoreConstants.DisposeTimeout);
        }
    }

    private static PostMessageRequestInternal CreatePostMessageRequestInternal(SendMessageRequestEntry entry,
        AttachmentUploads? uploads, bool checkResend)
        => new () {
            Uuid = entry.Uuid,
            Now = entry.Now,
            ChatId = entry.ChatId,
            LocalId = entry.LocalId,
            Text = entry.Text,
            RepliedEntryLid = entry.RepliedEntryLid,
            AttachmentUploads = uploads,
            ExistingMedia = entry.ExistingMedia,
            ClientId = entry.ClientId,
            NewChatEntryLocalId = entry.NewChatEntryLocalId,
            AfterSendMessageHandler = !entry.AfterSendMessageHandlerKey.IsNullOrEmpty()
                ? new AfterSendMessageHandler(entry.AfterSendMessageHandlerKey, entry.AfterSendMessageHandlerArgs)
                : null,
            CheckResend = checkResend,
        };

    private async Task<AttachmentUploads?> CreateAttachmentUploads(SendMessageRequestEntry entry, IReadOnlyList<Attachment>? sourceAttachments)
    {
        var attachEntries = entry.AttachFileRequests;
        if (attachEntries.Length == 0)
            return null;

        if (sourceAttachments is not null && sourceAttachments.Count != attachEntries.Length)
            throw StandardError.Internal("Source attachments count is not equal to attach file requests count.");

        var attachmentInfos = new List<AttachmentInfo>();
        try {
            for (var i = 0; i < attachEntries.Length; i++) {
                var attachEntry = attachEntries[i];
                var sourceAttachment = sourceAttachments?[i];
                AttachmentId? sourceAttachmentId = sourceAttachment?.Id;
                var sourcePreview = sourceAttachment is SourceAttachment s ? s.Preview : null;
                var uploadSessionId = attachEntry.UploadSessionId;
                Task<bool>? whenFilePermissionGranted = null;
                Task<AttachmentPreview>? attachmentPreviewTask = null;
                UploadSession? session = null;
                try {
                    session = await UploadSessions.TryGetSession(uploadSessionId).ConfigureAwait(false);
                }
                catch (Exception e) {
                    Log.LogError(e,
                        "Failed to get upload session '{UploadSessionId}' for attachment '{Attachment}'",
                        uploadSessionId,
                        attachEntry.FileName);
                }
                // Skip this attachment.
                if (session is null)
                    continue;

                if (session.IsCompleted) {
                    // If media was already uploaded, use it directly to display a preview.
                    // Do not try to access the file.
                    whenFilePermissionGranted = NeverGetFilePermission();

                    async Task<bool> NeverGetFilePermission()
                    {
                        await TaskExt.NeverEnding(CancellationToken.None).ConfigureAwait(false);
                        return false;
                    }

                    if (sourcePreview is null) {
                        var contentType = session.FileProvider.Metadata.FileType;
                        if (MediaTypeExt.IsVisualMedia(contentType))
                            sourcePreview = new FilePreview(UrlMapper.ContentUrl(session.MediaRef.BlobId));
                    }
                    attachmentPreviewTask = Task.FromResult(AttachmentPreview.From(sourcePreview));
                }
                else {
                    var fileProvider = session.FileProvider;
                    var canAccess = await fileProvider.CheckAccess().ConfigureAwait(false);
                    if (canAccess) {
                        var whenUserConsentGrantedTask = fileProvider.WhenUserConsentGranted();
                        whenFilePermissionGranted = whenUserConsentGrantedTask;
                        attachmentPreviewTask = GetAttachmentPreview();

                        async Task<AttachmentPreview> GetAttachmentPreview()
                        {
                            if (sourcePreview is not null)
                                return AttachmentPreview.From(sourcePreview);

                            var consentGranted = await whenUserConsentGrantedTask.ConfigureAwait(false);
                            if (!consentGranted)
                                return AttachmentPreview.NoFileAccess;

                            if (!MediaTypeExt.IsVisualMedia(fileProvider.Metadata.FileType))
                                return AttachmentPreview.NoPreview;

                            var preview = await fileProvider.GetPreview(Hub.StopToken).ConfigureAwait(false);
                            return AttachmentPreview.From(preview);
                        }
                    }
                }

                attachmentPreviewTask ??= Task.FromResult(AttachmentPreview.NoFileAccess);

                async Task CleanupRequest()
                    => await _requestsRepo.RemoveAttachRequest(entry.Uuid, attachEntry, CancellationToken.None)
                        .ConfigureAwait(false);

                var attachment = new Attachment(
                    attachEntry.FileName,
                    attachEntry.FileType,
                    attachEntry.FileLength,
                    new Size2D(attachEntry.Width, attachEntry.Height)) {
                    UploadSessionId = uploadSessionId,
                };
                attachment.Cleanups.Add(new AttachmentCleanup(AttachmentCleanupKind.PersistedPostMessageRequest, CleanupRequest));
                UploadSessions.AddReference(uploadSessionId);
                attachment.Cleanups.Add(AttachmentCleanupFactory.ForUploadSession(UploadSessions, uploadSessionId));
                if (sourceAttachmentId is not null)
                    AttachmentsState.Unregister(sourceAttachmentId.Value);
                AttachmentsState.Register(attachment);
                var attachmentInfo = new AttachmentInfo(attachment,
                    whenFilePermissionGranted ?? Task.FromResult(false),
                    attachmentPreviewTask);
                attachmentInfos.Add(attachmentInfo);
            }
            if (attachmentInfos.Count == 0)
                return null;

            var attachmentsController = Services.GetRequiredService<AttachmentsController>();
            var attachmentList = new AttachmentList();
            attachmentList.Subscribe(attachmentsController);
            foreach (var attachmentInfo in attachmentInfos) {
                var attachment = attachmentInfo.Attachment;
                attachmentList.Add(attachment);
                SetAttachmentPreview(attachment.Id, attachmentInfo.WhenFilePermissionGranted, attachmentInfo.GetPreview);
                var attachmentProgress = await AttachmentsState.GetProgress(attachment.Id, default).ConfigureAwait(false);
                if (attachmentProgress.IsReady || attachmentProgress.IsFailed)
                    continue;

                attachmentsController.ResumeUpload(attachment);
            }
            return new AttachmentUploads(attachmentList, AttachmentsState);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to create attachment uploads for post request '{Id}'", entry.Uuid);
            foreach (var attachmentInfo in attachmentInfos) {
                var attachment = attachmentInfo.Attachment;
                try {
                    AttachmentsState.Unregister(attachment.Id);
                    // NOTE(DF): during starting stored requests, it might be that there multiple consumers for the same upload it.
                    // So we can't delete the upload session until all stored requests are processed.
                    UploadSessions.ReleaseReference(attachment.UploadSessionId, false);
                }
                catch (Exception e2) {
                    Log.LogError(e2, "Failed to release attachment resources. Attachment: '{AttachmentId}', UploadSession: '{UploadSessionId}', File: '{FileName}'",
                        attachment.Id, attachment.UploadSessionId, attachment.FileName);
                }
            }
            throw;
        }
    }

    private void SetAttachmentPreview(
        AttachmentId attachmentId,
        Task<bool> whenFilePermissionGranted,
        Task<AttachmentPreview> getPreview)
    {
        if (getPreview.IsCompletedSuccessfully) {
            var preview = getPreview.GetAwaiter().GetResult();
            AttachmentsState.SetPreview(attachmentId, preview);
            return;
        }

        if (!whenFilePermissionGranted.IsCompleted)
            AttachmentsState.SetPreview(attachmentId, AttachmentPreview.PendingGetAccessRequest);

        _ = getPreview.ContinueWith(_ => {
            // Preview resolved.
            var preview = getPreview.GetAwaiter().GetResult();
#pragma warning disable VSTHRD002
            AttachmentsState.SetPreview(attachmentId, preview);
#pragma warning restore VSTHRD002
        }, TaskScheduler.Default);
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

    private async Task PostInternal(PostMessageRequestInternal request, TaskCompletionSource<ChatEntry?> resultSource, CancellationToken cancellationToken)
    {
        var discardSendRequest = false;
        ChatEntryId? ChatEntryId = null;
        try {
            DebugLog?.LogDebug(
                "Sending message: LocalId={LocalId}, Content='{Content}', ClientId='{ClientId}', NewChatEntryLocalId={NewChatEntryLocalId}",
                request.LocalId, request.Text.ToPrivate(), request.ClientId, request.NewChatEntryLocalId);
            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cancellationToken1 = cancellationTokenSource.Token;
            var sendingMessage = CreateAndRegisterSendingMessage(request, () => {
                discardSendRequest = true;
                cancellationTokenSource.Cancel();
            });
            if (request.NewChatEntryLocalId.HasValue)
                ChatEntryId = ChatEntryId.New(request.ChatId, request.NewChatEntryLocalId.Value);
            var result = ChatEntryId is null
                ? await ExecutePostRequestViaQueue(request, cancellationToken1).ConfigureAwait(false)
                : await TryGetExistentPostMessage(ChatEntryId, cancellationToken1).ConfigureAwait(false);

            var chatSendingMessages = GetChatSendingMessages(request.ChatId);
            if (result.IsValue(out var chatEntry1, out var exception)) {
                ChatEntryId = chatEntry1.Id;
                var hasAttachmentUploads = request.AttachmentUploads is not null;// chatEntry1.HasUploadingAttachments || chatEntry1.Attachments.Any(c => !(c.Media?.IsReady ?? false));
                chatSendingMessages.ConfirmMessageHasSent(sendingMessage, chatEntry1, Now, !hasAttachmentUploads);
                _mediaUploadsUI.Invalidate(sendingMessage);
                try {
                    if (hasAttachmentUploads)
                        chatEntry1 = await CompleteAttachmentUploads(
                                chatEntry1,
                                request,
                                chatSendingMessages,
                                sendingMessage,
                                cancellationToken1)
                            .ConfigureAwait(false);
                    resultSource.SetResult(chatEntry1);
                }
                catch (OperationCanceledException exception1) {
                    chatSendingMessages.ConfirmMessageFailedToSend(sendingMessage, exception1);
                    resultSource.TrySetCanceled(cancellationToken);
                }
            }
            else {
                chatSendingMessages.ConfirmMessageFailedToSend(sendingMessage, exception);
                if (cancellationToken1.IsCancellationRequested)
                    resultSource.TrySetCanceled(cancellationToken);
                else
                    resultSource.TrySetException(exception);
            }
        }
        catch (Exception e) {
            Log.LogError(e,
                "Failed to sent message. UUID={Uuid}, Text='{Text}'",
                request.Uuid, request.Text.ToPrivate());
            resultSource.TrySetException(e);
        }

        // Everything is completed. We can forget about this request and cleanup attachment resources.
        if (!resultSource.Task.IsCompleted)
            throw StandardError.Internal("Result source is not completed."); // Never should happen.

        if (discardSendRequest || !resultSource.Task.IsCanceled) {
            await DiscardStoredPostRequest(request.Uuid, cancellationToken).ConfigureAwait(false);
            if (request.AttachmentUploads is not null)
                await CleanupAttachments(request.Uuid, request.AttachmentUploads.Attachments.Items)
                    .ConfigureAwait(false);
            if (discardSendRequest && ChatEntryId is not null)
                await RemoveChatEntry(ChatEntryId, cancellationToken).ConfigureAwait(false);
        }

        var task = resultSource.Task;
        Result<ChatEntry?> result2 =
            task.IsCompletedSuccessfully ? new Result<ChatEntry?>(task.GetAwaiter().GetResult())
            : task.IsCanceled ? Result.NewError<ChatEntry?>(new OperationCanceledException())
            : Result.NewError<ChatEntry?>(task.Exception!);

        if (result2.IsValue(out var entry))
            _ = IncomingShareSuggestions?.Push(entry!.ChatId);

        InvokeAfterSendMessageHandler(
            request.AfterSendMessageHandler,
            result2);
    }

    private async Task<Result<ChatEntry>> ExecutePostRequestViaQueue(PostMessageRequestInternal request, CancellationToken cancellationToken1)
    {
        Result<ChatEntry> result;
        var queueMessageProcess = _messageProcessor.Enqueue(new PostMessageQueueItem(request), cancellationToken1);
        try {
            // NOTE: wait on the cancellation token to fail fast on send message request cancellation
            // (not await when the command will be processed by queue processor)
            var postResult = await queueMessageProcess.WhenCompleted.WaitAsync(cancellationToken1)
                .ConfigureAwait(false);
            var chatEntry = (ChatEntry)postResult!;
            result = new Result<ChatEntry>(chatEntry);
            DebugLog?.LogDebug(
                "Sent message: LocalId={LocalId}, Content='{Content}'",
                chatEntry.LocalId, chatEntry.Content.ToPrivate());
        }
        catch (Exception e) {
            // NOTE(DF): react on critical errors like have no longer have permissions to send a message to the chat.
            // Then we should discard this request and inform the user.
            Log.LogError(e,
                "Failed to sent message. UUID={Uuid}, Text='{Text}'",
                request.Uuid, request.Text.ToPrivate());
            result = Result.NewError<ChatEntry>(e);
        }

        return result;
    }

    private async Task<Result<ChatEntry>> TryGetExistentPostMessage(ChatEntryId chatEntryId, CancellationToken cancellationToken1)
    {
        Result<ChatEntry> result;
        var chatEntry = await Hub.Chats.GetEntry(Hub.Session, chatEntryId, cancellationToken1).ConfigureAwait(false);
        if (chatEntry is not null)
            result = Result.New(chatEntry);
        else
            result = Result.NewError<ChatEntry>(StandardError.Internal($"Can't find chat entry with id '{chatEntryId}'."));
        return result;
    }

    private async Task<ChatEntry?> CompleteAttachmentUploads(
        ChatEntry chatEntry,
        PostMessageRequestInternal request,
        ChatSendingMessages chatSendingMessages,
        SendingMessage sendingMessage,
        CancellationToken cancellationToken)
    {
        if (request.AttachmentUploads is null) {
            // NOTE(DF): this should never happen.
            Log.LogError("Attachment uploads are not set for message with local id '{LocalId}'", chatEntry.LocalId);
            chatSendingMessages.ConfirmMessageHasSent(sendingMessage, chatEntry, Now, true);
            return chatEntry;
        }

        if (!request.NewChatEntryLocalId.HasValue)
            // Persist that the message has been sent before we continue further processing with attachments.
            await _requestsRepo
                .MarkMessageHasCreated(request.Uuid, chatEntry.LocalId, cancellationToken)
                .ConfigureAwait(false);

        ChatEntry? chatEntry1;
        await request.AttachmentUploads.WhenUploaded.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (request.AttachmentUploads.Attachments.Count == 0 && chatEntry.Content.IsNullOrEmpty()) {
            // Remove the message if it has no attachments and no content.
            await RemoveChatEntry(chatEntry.Id, cancellationToken).ConfigureAwait(false);
            chatEntry1 = null;
        }
        else {
            var mediaContents = (await request.AttachmentUploads.Attachments.Items
                    .Select(c => Hub.UploadSessions.TryGetSession(c.UploadSessionId))
                    .Collect(cancellationToken)
                    .ConfigureAwait(false))
                .Select(c => c?.MediaRef)
                .SkipNullItems()
                .ToDictionary(c => c.MediaId, c => c);
            var entryAttachments = chatEntry.Attachments
                .Select(c => new { Attachment = c, MediaRef = mediaContents.GetValueOrDefault(c.MediaId) })
                .Where(c => c.MediaRef is not null)
                .Select(c => new ChatEntryAttachment {
                    Id = c.Attachment.Id,
                    MediaId = c.MediaRef!.MediaId,
                    ThumbnailMediaId = c.MediaRef.ThumbnailMediaId,
                }).ToArray();

            // Finalize the message with attachments.
            var cmd = new Chats_UpsertEntry(Session, chatEntry.ChatId, chatEntry.LocalId) {
                Text = chatEntry.Content,
                Attachments = entryAttachments,
                HasUploadingAttachments = false,
            };
            chatEntry1 = await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        }

        chatSendingMessages.ConfirmMessageAttachmentsHaveSent(sendingMessage, chatEntry1, Now);
        // Delete upload info with delay to avoid flickering in the UI.
        // Linked to service lifetime so it's cancelled on dispose instead of running against a disposed scope.
        var serviceToken = _cancellationTokenSource.Token;
        _ = BackgroundTask.Run(async () => {
                try {
                    await Task.Delay(TimeSpan.FromMinutes(1), serviceToken).ConfigureAwait(false);
                    _mediaUploadsUI.Delete(sendingMessage);
                }
                catch (OperationCanceledException) {
                    // Service is being disposed - skip cleanup.
                }
            },
            serviceToken);
        return chatEntry1;
    }

    private void InvokeAfterSendMessageHandler(AfterSendMessageHandler? afterSendMessageHandler, Result<ChatEntry?> result)
    {
        if (afterSendMessageHandler is null)
            return;

        IAfterSendMessageHandler handler = afterSendMessageHandler.Key switch {
            AfterSendMessageHandlerKeys.IncomingShare => Hub.Services.GetRequiredService<IncomingShareAfterSendMessageHandler>(),
            _ => throw new InvalidOperationException($"Unknown after send message handler key '{afterSendMessageHandler.Key}'")
        };

        handler.Invoke(afterSendMessageHandler.Args, result);
    }

    private async Task RemoveChatEntry(ChatEntryId chatEntryId, CancellationToken cancellationToken)
    {
        var cmd = new Chats_RemoveEntry(Session, chatEntryId.ChatId, chatEntryId.LocalId);
        await Commander.Run(cmd, cancellationToken).ConfigureAwait(false);
    }

    // Nested types

    public sealed record PostMessageRequestInternal : IHasId<string>, ISanitized
    {
        public required string Uuid { get; init; }
        public required Moment Now { get; init; }
        public required ChatId ChatId { get; init; }
        public long? LocalId { get; init; }
        public string Text { get => Sanitizer.MaskPrivate(field); init; } = "";
        public Option<long?> RepliedEntryLid { get; init; }
        public AttachmentUploads? AttachmentUploads { get; init; }
        public IReadOnlyList<MediaRef> ExistingMedia { get; init; } = [];
        public string ClientId { get; init; } = "";
        public long? NewChatEntryLocalId { get; init; }
        public AfterSendMessageHandler? AfterSendMessageHandler { get; init; }
        public bool CheckResend { get; init; }

        string IHasId<string>.Id => Uuid;
    }

    private sealed record AttachmentInfo(
        Attachment Attachment,
        Task<bool> WhenFilePermissionGranted,
        Task<AttachmentPreview> GetPreview);
}
