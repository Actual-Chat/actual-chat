
namespace ActualChat.UI.Blazor.App.Services;

partial class SendingMessages
{
    private static readonly TimeSpan ProcessCommandTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProcessCommandRetryDelay = TimeSpan.FromSeconds(1);

    private async Task<object?> ProcessQueueItem(PostMessageQueueItem command, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("-> ProcessQueueItem. Text: '{Text}'", command.Request.Text.ToPrivate());
        while (true) {
            using var cts = cancellationToken.CreateLinkedTokenSource();
            cts.CancelAfter(ProcessCommandTimeout);
            try {
                ChatEntry chatEntry = await ProcessCommand(command, cts.Token).ConfigureAwait(false);
                DebugLog?.LogDebug("<- ProcessQueueItem. Text: '{Text}'", command.Request.Text.ToPrivate());
                return chatEntry;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                if (!IsTransientError(e)) {
                    Log.LogError(e,
                        "ProcessQueueItem permanently failed for '{Text}'",
                        command.Request.Text.ToPrivate());
                    throw;
                }
                if (e is OperationCanceledException)
                    Log.LogInformation("ProcessQueueItem failed (OperationCanceledException) for '{Text}', retrying in {Delay}s",
                        command.Request.Text.ToPrivate(), ProcessCommandRetryDelay.TotalSeconds);
                else
                    Log.LogWarning(e,
                        "ProcessQueueItem failed for '{Text}', retrying in {Delay}s",
                        command.Request.Text.ToPrivate(), ProcessCommandRetryDelay.TotalSeconds);
                await Task.Delay(ProcessCommandRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<ChatEntry> ProcessCommand(PostMessageQueueItem item, CancellationToken cancellationToken)
    {
        var request = item.Request;
        if (request.CheckResend) {
            var chatEntry1 = await TryFindPreviouslySentEntry(request.ChatId, request.ClientId, cancellationToken).ConfigureAwait(false);
            if (chatEntry1 is not null)
                return chatEntry1;
        }
        var mediaIds = await ReserveMediaIds(request, cancellationToken).ConfigureAwait(false);
        var attachments = mediaIds
            .Select(x => new ChatEntryAttachment { MediaId = x })
            .Concat(request.ExistingMedia.Select(x => new ChatEntryAttachment {
                MediaId = x.MediaId,
                ThumbnailMediaId = x.ThumbnailMediaId,
            }))
            .ToArray();
        // The request's Uuid survives both the retry loop above and an app restart, so a resend of a
        // command the server already applied replays its result instead of posting a second message.
        var cmd = new Chats_UpsertEntry {
            Uuid = request.Uuid,
            Session = Session,
            ChatId = request.ChatId,
            LocalId = request.LocalId,
            Text = request.Text,
            RepliedEntryLid = request.RepliedEntryLid,
            ClientId = request.ClientId,
            Attachments = attachments,
            HasUploadingAttachments = request.AttachmentUploads is not null,
        };
        // // Simulate long sending
        // await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
        var chatEntry = await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        if (chatEntry.Attachments.Length > 0) {
            // Refetch the entry with attachments with media populated.
            var chatEntry1 = await Chats.GetEntry(Session, chatEntry.Id, cancellationToken).ConfigureAwait(false);
            // Should always be non-null, but just in case.
            if (chatEntry1 is not null)
                chatEntry = chatEntry1;
        }
        var isNewMessage = cmd.LocalId is null;
        if (isNewMessage)
            AnalyticEvents.RaiseMessagePosted(
                cmd.RepliedEntryLid.HasValue,
                !cmd.Text.IsNullOrEmpty(),
                cmd.Attachments.Length);
        return chatEntry;
    }

    private async Task<ChatEntry?> TryFindPreviouslySentEntry(ChatId chatId, string clientId, CancellationToken cancellationToken)
    {
        var range = await Chats.GetIdRange(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (range.IsEmpty)
            return null;

        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return null;

        var ownAuthor = chat.Rules.Author;
        if (ownAuthor is null)
            return null;

        var ownAuthorId = ownAuthor.Id;
        var entryReader = Chats.NewEntryReader(Session, chatId);
        var counter = 0;
        const int maxResendScanCount = 200; // Scan the last 200 messages
        await foreach (var chatEntry1 in entryReader.ReadReverse(range, cancellationToken).ConfigureAwait(false)) {
            if (chatEntry1.AuthorId == ownAuthorId && chatEntry1.ClientId == clientId)
                return chatEntry1;

            counter++;
            if (counter >= maxResendScanCount)
                break;
        }
        return null;
    }

    private async Task<MediaId[]> ReserveMediaIds(PostMessageRequestInternal request, CancellationToken cancellationToken)
    {
        if (request.AttachmentUploads is null)
            return [];

        // TODO(DF): convert to durable commands
        var mediaIds = new List<MediaId>();
        foreach (var attachment in request.AttachmentUploads.Attachments.Items) {
            var sessionId = attachment.UploadSessionId;
            var uploadMediaId = await UploadSessions.GetOrReserveMedia(sessionId, cancellationToken).ConfigureAwait(false);
            mediaIds.Add(uploadMediaId);
        }
        return mediaIds.ToArray();
    }

    private static bool IsTransientError(Exception e)
        // Transient errors that are possible here:
        // - TimeoutException is thrown by Errors.ConnectTimeout when the peer is unreachable.
        // - OperationCanceledException covers possible server-side cancellations
        //   and ProcessCommandTimeout.
        // Anything else (validation, business constraints, etc.) must fail so the user sees the error.
        // Note that:
        // - RpcRerouteException shouldn't be thrown here, but it's still covered by OperationCanceledException
        // - RpcReconnectFailedException also shouldn't be thrown here.
        => e is OperationCanceledException or TimeoutException;

    // Nested types
    public record PostMessageQueueItem(PostMessageRequestInternal Request);
}
