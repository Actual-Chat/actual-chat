
namespace ActualChat.UI.Blazor.App.Services;

partial class SendingMessages
{
    private static readonly TimeSpan ProcessCommandTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProcessCommandRetryDelay = TimeSpan.FromSeconds(1);

    private async Task<object?> ProcessQueueItem(PostMessageQueueItem command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        DebugLog?.LogDebug("-> ProcessQueueItem. Text: '{Text}'", request.Text.ToPrivate());
        while (true) {
            using var cts = cancellationToken.CreateLinkedTokenSource();
            cts.CancelAfter(ProcessCommandTimeout);
            try {
                // The request that carries the created location's id replaces the original, so a retry
                // after a failed post doesn't create the location again
                request = await EnsureSharedLocation(request, cts.Token).ConfigureAwait(false);
                ChatEntry chatEntry = await ProcessCommand(request, cts.Token).ConfigureAwait(false);
                DebugLog?.LogDebug("<- ProcessQueueItem. Text: '{Text}'", request.Text.ToPrivate());
                return chatEntry;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested
                && !_cancellationTokenSource.IsCancellationRequested) {
                // The send was cancelled rather than the service stopped: a live share created above
                // must not run on without its entry, and nothing else knows its id
                if (request.LocationDiff?.LiveDuration > TimeSpan.Zero && request.NewLocationId is { } createdId)
                    await StopSharedLocation(request.ChatId, createdId).ConfigureAwait(false);
                throw;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                if (!IsTransientError(e)) {
                    Log.LogError(e,
                        "ProcessQueueItem permanently failed for '{Text}'",
                        request.Text.ToPrivate());
                    throw;
                }
                if (e is OperationCanceledException)
                    Log.LogInformation("ProcessQueueItem failed (OperationCanceledException) for '{Text}', retrying in {Delay}s",
                        request.Text.ToPrivate(), ProcessCommandRetryDelay.TotalSeconds);
                else
                    Log.LogWarning(e,
                        "ProcessQueueItem failed for '{Text}', retrying in {Delay}s",
                        request.Text.ToPrivate(), ProcessCommandRetryDelay.TotalSeconds);
                await Task.Delay(ProcessCommandRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<ChatEntry> ProcessCommand(
        PostMessageRequestInternal request,
        CancellationToken cancellationToken)
    {
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
            QuotedText = request.QuotedText,
            ClientId = request.ClientId,
            Attachments = attachments,
            HasUploadingAttachments = request.AttachmentUploads is not null,
            LocationId = request.NewLocationId,
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
        if (isNewMessage && request.NewLocationId is null)
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

    private async Task<PostMessageRequestInternal> EnsureSharedLocation(
        PostMessageRequestInternal request,
        CancellationToken cancellationToken)
    {
        if (request.NewLocationId is not null || request.LocationDiff is not { } locationDiff)
            return request;

        // The Uuid is derived from the request's, so a resend of a create the server already applied
        // replays its result instead of minting a second location; the suffix keeps it apart from the
        // entry's own upsert, which the server deduplicates by the same key.
        var cmd = new SharedLocations_Change {
            Uuid = request.Uuid + "-location",
            Session = Session,
            ChatId = request.ChatId,
            Id = null,
            Change = Change.Create(locationDiff),
        };
        var created = await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        if (created is null)
            throw StandardError.Internal("Failed to create a shared location.");

        await _requestsRepo.MarkLocationWasCreated(request.Uuid, created.Id, cancellationToken)
            .ConfigureAwait(false);
        return request with { NewLocationId = created.Id };
    }

    private async Task StopSharedLocation(ChatId chatId, SharedLocationId locationId)
    {
        try {
            using var cts = new CancellationTokenSource(ProcessCommandTimeout);
            var cmd = new SharedLocations_Change {
                Session = Session,
                ChatId = chatId,
                Id = locationId,
                Change = Change.Remove<SharedLocationDiff>(),
            };
            await Commander.Call(cmd, cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to stop the shared location '{LocationId}' of a cancelled send", locationId);
        }
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
