using ActualChat.Media;

namespace ActualChat.UI.Blazor.App.Services;

partial class SendingMessages
{
private async Task<object?> ProcessQueueItem(PostMessageQueueItem command, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("-> ProcessQueueItem. Text: '{Text}'", command.Request.Text);
        ChatEntry chatEntry = await ProcessCommand(command, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogDebug("<- ProcessQueueItem. Text: '{Text}'", command.Request.Text);
        return chatEntry;
    }

    private async Task<ChatEntry> ProcessCommand(PostMessageQueueItem item, CancellationToken cancellationToken)
    {
        var request = item.Request;
        if (request.CheckResend) {
            var chatEntry1 = await TryFindPreviouslySendEntry(request.ChatId, request.ClientId, cancellationToken).ConfigureAwait(false);
            if (chatEntry1 is not null)
                return chatEntry1;
        }
        var mediaIds = await ReservedMediaIds(request, cancellationToken).ConfigureAwait(false);
        var textEntryAttachments = mediaIds
            .Select(x => new TextEntryAttachment { MediaId = x })
            .ToArray();
        var cmd = new Chats_UpsertTextEntry(Session, request.ChatId, request.LocalId, request.Text, request.RepliedEntryLid) {
            ClientId = request.ClientId,
            EntryAttachments = textEntryAttachments,
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

    private async Task<MediaId[]> ReservedMediaIds(PostMessageRequestInternal request, CancellationToken cancellationToken)
    {
        if (request.AttachmentUploads is null)
            return [];

        // TODO(DF): convert to durable commands
        var mediaIds = new List<MediaId>();
        var attachmentRegistry = Hub.AttachmentRegistry;
        try {
            foreach (var attachment in request.AttachmentUploads.Attachments.Items) {
                var reservedMediaId = attachmentRegistry.GetReservedMediaIdNonComputed(attachment.Id);
                if (reservedMediaId is not null)
                    continue;

                var sessionId = attachment.UploadSessionId;
                var metadata = await attachmentRegistry.GetUploadSessionMetadata(sessionId, cancellationToken)
                    .ConfigureAwait(false);
                // TODO(DF): review how we choose media scope and whether we need a new media id here or not.
                var mediaScope = request.ChatId.Value;
                var reserveCmd = new Medias_ReserveMedia(Session, mediaScope) { Metadata = metadata };
                var mediaId = await Commander.Call(reserveCmd, cancellationToken).ConfigureAwait(false);
                attachmentRegistry.SetReservedMediaId(attachment.Id, mediaId);
                await _requestsRepo.SetReservedMediaId(request.Uuid, attachment.UploadSessionId, mediaId, cancellationToken).ConfigureAwait(false);
                mediaIds.Add(mediaId);
            }
        }
        finally {
            await _requestsRepo.Flush(cancellationToken).ConfigureAwait(false);
        }
        return mediaIds.ToArray();
    }

    // Nested types
    public record PostMessageQueueItem(PostMessageRequestInternal Request);
}
