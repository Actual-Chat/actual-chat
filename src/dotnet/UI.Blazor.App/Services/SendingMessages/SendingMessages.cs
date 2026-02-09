using ActualChat.Media;
using ActualChat.Messaging;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

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
    private IChats Chats => Hub.Chats;
    private Moment Now => Clocks.SystemClock.Now;

    public Task WhenStoredRequestsProcessed => _whenStoredRequestsProcessed;

    public SendingMessages(AppUIHub hub) : base(hub)
    {
        DebugLog?.LogDebug("SendingMessages constructor");
        _requestsRepo = new SendMessageRequestsRepo(hub);
        _triggers = Services.GetRequiredService<ChatSendingMessagesTriggers>();
        _mediaUploadsUI = new MediaUploadsUI(_triggers);
        _whenStoredRequestsProcessed = BackgroundTask.Run(StartStoredPostRequests);
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

    public async Task<FilesUploadHandle?> Upload(ImmutableArray<Attachment> attachments)
    {
        if (attachments.Length == 0)
            return null;

        var uploadEntries = new List<UploadFileRequestEntry>();
        foreach (var attachment in attachments) {
            var uploadSessionId = attachment.UploadSessionId;
            if (uploadSessionId.IsNullOrEmpty()) {
                if (attachment.FileProvider is not { } fileProvider)
                    throw new InvalidOperationException($"Can't initialize upload for attachment '{attachment.Id}'. No file provider assigned.");

                var uploadSession = await UploadSessions.CreateSession(fileProvider).ConfigureAwait(false);
                uploadSessionId = uploadSession.SessionId;
            }
            var attachEntry = new UploadFileRequestEntry(uploadSessionId, attachment.FileName, attachment.FileType, attachment.Length, attachment.Width, attachment.Height, attachment.Id);
            uploadEntries.Add(attachEntry);
        }
        var upload = new FilesUpload(attachments, uploadEntries.ToArray());
        return _filesUploadRegistry.Register(upload);
    }

    public async Task<Task<ChatEntry?>> Send(SendMessageRequest cmd, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("Post '{Text}'", cmd.Text);
        var now = Clocks.SystemClock.Now;
        var resultSource = TaskCompletionSourceExt.New<ChatEntry?>();
        await _whenStoredRequestsProcessed.ConfigureAwait(false);
        var uuid = Ulid.NewUlid().ToString();
        var filesUpload = cmd.Uploads is not null ? _filesUploadRegistry.Get(cmd.Uploads) : null;
        var entry = CreateStoredSendRequest(uuid, now, cmd, filesUpload);
        DebugLog?.LogDebug("About to store post request '{Text}'", cmd.Text);
        await _requestsRepo.Add(entry, cancellationToken).ConfigureAwait(false);

        if (filesUpload is not null)
            filesUpload.AddReference();
        var uploads = await CreateAttachmentUploads(entry, filesUpload?.GetAttachments()).ConfigureAwait(false);
        var requestInternal = CreatePostMessageRequestInternal(entry, uploads, false);
        _ = BackgroundTask.Run(async () => {
            DebugLog?.LogDebug("About to post internal '{Text}'", cmd.Text);
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
            !entry.AfterSendMessageHandlerKey.IsNullOrEmpty() ? new AfterSendMessageHandler(entry.AfterSendMessageHandlerKey, entry.AfterSendMessageHandlerArgs) : null,
            checkResend);

    private record AttachExtra(Task<bool> WhenFilePermissionGranted, Task<string> GetPreviewUrl);

    private record UploadAttachment(
        string FileName,
        string FileType,
        long Length,
        int Width,
        int Height,
        AttachExtra Extras) : Attachment(FileName, FileType, Length, Width, Height)
    {
        public bool NoFileAccess { get; init; }
    }

    private async Task<AttachmentUploads?> CreateAttachmentUploads(SendMessageRequestEntry entry, IReadOnlyList<Attachment>? sourceAttachments)
    {
        var attachEntries = entry.AttachFileRequests;
        if (attachEntries.Length == 0)
            return null;

        if (sourceAttachments is not null && sourceAttachments.Count != attachEntries.Length)
            throw StandardError.Internal("Source attachments count is not equal to attach file requests count.");

        var attachments = new List<UploadAttachment>();
        var attachmentRegistry = Hub.AttachmentRegistry;
        for (var i = 0; i < attachEntries.Length; i++) {
            var attachEntry = attachEntries[i];
            var sourceAttachment = sourceAttachments?[i];
            var previewUrl = "";
            Task<string> getPreviewUrl = Task.FromResult("");
            AttachmentId? sourceAttachmentId = null;
            if (sourceAttachment is SourceAttachment s) {
                sourceAttachmentId = s.Id;
                previewUrl = s.PreviewUrl;
                getPreviewUrl = Task.FromResult(previewUrl);
            }
            var uploadSessionId = attachEntry.UploadSessionId;
            // TODO: to think what to do with this. For now UploadApp is responsible for resuming stored sessions.
            var attachmentIsOk = false;
            Task<bool>? whenFilePermissionGranted = null;
            MediaContent? mediaContent = null;
            try {
                var session = await UploadSessions.TryGetSession(uploadSessionId).ConfigureAwait(false);
                if (session is not null) {
                    if (session.Status is UploadStatus.Completed && session.MediaContent is not null) {
                        // If media was already uploaded, use it directly to display a preview.
                        // Do not try to access the file.
                        mediaContent = session.MediaContent;
                        whenFilePermissionGranted = NeverGetFilePermission();
                        async Task<bool> NeverGetFilePermission() {
                            await TaskExt.NeverEnding(CancellationToken.None).ConfigureAwait(false);
                            return false;
                        }
                        if (previewUrl.IsNullOrEmpty()) {
                            var contentType = session.FileProvider.Metadata.FileType;
                            if (MediaTypeExt.IsVisualMedia(contentType))
                                previewUrl = UrlMapper.ContentUrl(session.MediaContent.ContentId);
                        }
                        if (!previewUrl.IsNullOrEmpty())
                            getPreviewUrl = Task.FromResult(previewUrl);
                        attachmentIsOk = true;
                    }
                    else {
                        var fileProvider = session.FileProvider;
                        var canAccess = await fileProvider.CheckAccess().ConfigureAwait(false);
                        if (canAccess) {
                            var whenUserConsentGrantedTask = fileProvider.WhenUserConsentGranted();
                            if (!previewUrl.IsNullOrEmpty())
                                getPreviewUrl = Task.FromResult(previewUrl);
                            else if (MediaTypeExt.IsVisualMedia(fileProvider.Metadata.FileType)) {
                                getPreviewUrl = GetPreviewUrl();
                                async Task<string> GetPreviewUrl() {
                                    var consentGranted = await whenUserConsentGrantedTask.ConfigureAwait(false);
                                    if (!consentGranted)
                                        return "";

                                    var preview2 = await fileProvider.GetPreviewUrl().ConfigureAwait(false);
                                    return preview2;
                                }
                            }
                            whenFilePermissionGranted = whenUserConsentGrantedTask;
                            attachmentIsOk = true;
                        }
                    }
                }
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to get upload session '{UploadSessionId}'", uploadSessionId);
                // Intended
            }

            async Task CleanupRequest()
                => await _requestsRepo.RemoveAttachRequest(entry.Uuid, attachEntry, CancellationToken.None).ConfigureAwait(false);

            var attachExtra = new AttachExtra(whenFilePermissionGranted ?? Task.FromResult(false), getPreviewUrl);
            var attachment = new UploadAttachment(
                attachEntry.FileName,
                attachEntry.FileType,
                attachEntry.FileLength,
                attachEntry.Width,
                attachEntry.Height,
                attachExtra) {
                UploadSessionId = uploadSessionId,
                NoFileAccess = !attachmentIsOk
            };
            attachment.Cleanups.Add(new AttachmentCleanup(AttachmentCleanupKind.PersistedPostMessageRequest, CleanupRequest));
            attachment.Cleanups.Add(AttachmentCleanupFactory.ForUploadSession(UploadSessions, uploadSessionId));
            if (sourceAttachmentId is not null)
                attachmentRegistry.Unregister(sourceAttachmentId.Value);
            attachmentRegistry.Register(attachment);
            if (!attachmentIsOk)
                attachmentRegistry.SetPreviewState(attachment.Id, AttachmentPreviewState.NoFileAccess);
            else if (mediaContent is not null)
                attachmentRegistry.SetMediaContent(attachment.Id, mediaContent);
            attachments.Add(attachment);
        }
        if (attachments.Count == 0)
            return null;

        var attachmentsController = Services.GetRequiredService<AttachmentsController>();
        var attachmentList = new AttachmentList();
        attachmentList.Subscribe(attachmentsController);
        foreach (var attachment in attachments) {
            attachmentList.Add(attachment);
            if (attachment.NoFileAccess)
                continue;
            var mediaStatus = await attachmentRegistry.GetMediaStatus(attachment.Id, default).ConfigureAwait(false);
            if (mediaStatus is null || mediaStatus.Status is not MediaStatus.Ready && mediaStatus.Status is not MediaStatus.Failed)
                await attachmentsController.ResumeUpload(attachment).ConfigureAwait(false);
        }

        foreach (var attachment in attachments) {
            var mediaStatus = await attachmentRegistry.GetMediaStatus(attachment.Id, default).ConfigureAwait(false);
            if (mediaStatus is null || mediaStatus.Status is not MediaStatus.Ready)
                SubscribeFilePermissionsGranted(attachment.Id, attachment.Extras);
            SubscribePreviewResolved(attachment.Id, attachment.Extras);
        }

        return new AttachmentUploads(attachmentList, Hub.AttachmentRegistry);
    }

    private void SubscribePreviewResolved(AttachmentId attachmentId, AttachExtra extras)
    {
        // if (extras.GetPreviewUrl.IsCompleted)
        //     return;

        _ =  extras.GetPreviewUrl.ContinueWith(t => {
            // Preview resolved.
#pragma warning disable VSTHRD002
            Hub.AttachmentRegistry.SetPreviewState(attachmentId, AttachmentPreviewState.Preview(t.Result));
 #pragma warning restore VSTHRD002
        }, TaskScheduler.Default);
    }

    private void SubscribeFilePermissionsGranted(AttachmentId attachmentId, AttachExtra extras)
        => _ = extras.WhenFilePermissionGranted.ContinueWith(t => {
            if (t.Result)
                return;

            // File permission was denied.
            Hub.AttachmentRegistry.SetPreviewState(attachmentId, AttachmentPreviewState.NoFileAccess);
            // Hub.AttachmentRegistry.Update(attachmentId,
            //     a => a with {
            //         Failed = true,
            //     });
        }, TaskScheduler.Default);

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
        TextEntryId? textEntryId = null;
        try {
            DebugLog?.LogDebug("Sending message: LocalId={LocalId}, Content='{Content}', ClientId='{ClientId}', NewChatEntryLocalId={NewChatEntryLocalId}",
                request.LocalId, request.Text, request.ClientId, request.NewChatEntryLocalId);
            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cancellationToken1 = cancellationTokenSource.Token;
            var sendingMessage = CreateAndRegisterSendingMessage(request, () => {
                discardSendRequest = true;
                cancellationTokenSource.Cancel();
            });
            if (request.NewChatEntryLocalId.HasValue)
                textEntryId = TextEntryId.New(request.ChatId, request.NewChatEntryLocalId.Value);
            var result = textEntryId is null
                ? await ExecutePostRequestViaQueue(request, cancellationToken1).ConfigureAwait(false)
                : await TryGetExistentPostMessage(textEntryId, cancellationToken1).ConfigureAwait(false);

            var chatSendingMessages = GetChatSendingMessages(request.ChatId);
            if (result.IsValue(out var chatEntry1, out var exception)) {
                textEntryId = (TextEntryId)chatEntry1.Id;
                var hasAttachmentUploads = request.AttachmentUploads is not null;// chatEntry1.HasAttachmentUploads || chatEntry1.Attachments.Any(c => !(c.Media?.IsReady ?? false));
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
                    resultSource.TrySetCanceled();
                }
            }
            else {
                chatSendingMessages.ConfirmMessageFailedToSend(sendingMessage, exception);
                if (cancellationToken1.IsCancellationRequested)
                    resultSource.TrySetCanceled();
                else
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
        if (!resultSource.Task.IsCompleted)
            throw StandardError.Internal("Result source is not completed."); // Never should happen.

        if (discardSendRequest || !resultSource.Task.IsCanceled) {
            await DiscardStoredPostRequest(request.Uuid, cancellationToken).ConfigureAwait(false);
            if (request.AttachmentUploads is not null)
                await CleanupAttachments(request.Uuid, request.AttachmentUploads.Attachments.Items)
                    .ConfigureAwait(false);
            if (discardSendRequest && textEntryId is not null)
                await RemoveChatEntry(textEntryId, cancellationToken).ConfigureAwait(false);
        }

        var task = resultSource.Task;
        Result<ChatEntry?> result2 =
            task.IsCompletedSuccessfully ? new Result<ChatEntry?>(task.Result)
            : task.IsCanceled ? Result.NewError<ChatEntry?>(new OperationCanceledException())
            : Result.NewError<ChatEntry?>(task.Exception!);

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
            DebugLog?.LogDebug("Sent message: LocalId={LocalId}, Content='{Content}'",
                chatEntry.LocalId,
                chatEntry.Content);
        }
        catch (Exception e) {
            // NOTE(DF): react on critical errors like have no longer have permissions to send a message to the chat.
            // Then we should discard this request and inform the user.
            Log.LogError(e, "Failed to sent message. UUID={Uuid}, Text='{Text}'", request.Uuid, request.Text);
            result = Result.NewError<ChatEntry>(e);
        }

        return result;
    }

    private async Task<Result<ChatEntry>> TryGetExistentPostMessage(TextEntryId chatEntryId, CancellationToken cancellationToken1)
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

        ChatEntry? chatEntry1 = chatEntry;
        await request.AttachmentUploads.WhenUploaded.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (request.AttachmentUploads.Attachments.Count == 0 && chatEntry.Content.IsNullOrEmpty()) {
            // Remove the message if it has no attachments and no content.
            await RemoveChatEntry((TextEntryId)chatEntry.Id, cancellationToken).ConfigureAwait(false);
            chatEntry1 = null;
        }

        chatSendingMessages.ConfirmMessageAttachmentsHaveSent(sendingMessage, chatEntry1, Now);
        // Delete upload info with delay to avoid flickering in the UI.
        _ = BackgroundTask.Run(async () => {
                await Task.Delay(TimeSpan.FromMinutes(1), CancellationToken.None).ConfigureAwait(false);
                _mediaUploadsUI.Delete(sendingMessage);
            },
            CancellationToken.None);
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

    // private async Task<ChatEntry?> CreateAttachments(ChatEntry chatEntry, AttachmentUploads attachmentUploads, CancellationToken cancellationToken = default)
    // {
    //     // An attachment should have several states: loading, loading error, canceled (removed), loaded.
    //     // When an attachment has entered the `loading error` state, you can repeat the loading or cancel it entirely.
    //     await attachmentUploads.WhenUploaded.WaitAsync(cancellationToken).ConfigureAwait(false);
    //     // When all attachments are loaded, we can continue execution.
    //     var entryAttachments = CreateTextEntryAttachments(attachmentUploads);
    //     if (entryAttachments.Length == 0 && chatEntry.Content.IsNullOrEmpty()) {
    //         // If there are no loaded attachments and the ChatEntry Content is empty,
    //         // then we delete this ChatEntry altogether.
    //         await RemoveChatEntry((TextEntryId)chatEntry.Id, cancellationToken).ConfigureAwait(false);
    //         return null;
    //     }
    //     // If there are loaded attachments,
    //     // then we add them to the ChatEntry and mark that there are no more loadings.
    //     // NOTE(DF): may be better to introduce a new command for this.
    //     var cmd = new Chats_UpsertTextEntry(Session, chatEntry.ChatId, chatEntry.LocalId, chatEntry.Content) {
    //         HasAttachmentUploads = false,
    //         EntryAttachments = entryAttachments,
    //     };
    //     return await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    // }

    private async Task RemoveChatEntry(TextEntryId chatEntryId, CancellationToken cancellationToken)
    {
        var cmd1 = new Chats_RemoveTextEntry(Session, chatEntryId.ChatId, chatEntryId.LocalId);
        await Commander.Run(cmd1, cancellationToken).ConfigureAwait(false);
    }

    // private TextEntryAttachment[] CreateTextEntryAttachments(AttachmentUploads attachmentUploads)
    // {
    //     var registry = Hub.AttachmentRegistry;
    //     var entryAttachments = attachmentUploads.Attachments.Items
    //         .Select(x => registry.GetState(x.Id))
    //         .Where(x => x.Uploaded)
    //         .Select(x => new TextEntryAttachment {
    //             MediaId = x.MediaId!,
    //             ThumbnailMediaId = x.ThumbnailMediaId,
    //         }).ToArray();
    //     return entryAttachments;
    // }

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
        AfterSendMessageHandler? AfterSendMessageHandler,
        bool CheckResend
    ) : IHasId<string>
    {
        string IHasId<string>.Id => Uuid;
    }
}
