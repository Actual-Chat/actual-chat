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
            var chatEntry1 = await TryFindPreviouslySendEntry(request.ChatId, request.ClientId, cancellationToken).ConfigureAwait(false);
            if (chatEntry1 is not null)
                return chatEntry1;
        }
        var mediaIds = await ReserveMediaIds(request, cancellationToken).ConfigureAwait(false);
        var attachments = mediaIds
            .Select(x => new ChatEntryAttachment { MediaId = x })
            .ToArray();
        var cmd = new Chats_UpsertTextEntry(Session, request.ChatId, request.LocalId) {
            Text = request.Text,
            RepliedEntryLid = request.RepliedEntryLid,
            ClientId = request.ClientId,
            Attachments = attachments,
        };
        // // Simulate long sending
        // await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
        var postResult = await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);
        var chatEntry = postResult.Result.Value;
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

    private async Task<ChatEntry?> TryFindPreviouslySendEntry(ChatId chatId, string clientId, CancellationToken cancellationToken)
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
            if (chatEntry1.AuthorId == ownAuthorId && OrdinalEquals(chatEntry1.ClientId, clientId))
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

    // Nested types
    public record PostMessageQueueItem(PostMessageRequestInternal Request);
}
