namespace ActualChat.UI.Blazor.App.Services;

partial class SendingMessages
{
    private async Task StartStoredPostRequests()
    {
        DebugLog?.LogDebug("StartStoredPostRequests");
        var cancellationToken = CancellationToken.None;
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

    private async Task DiscardStoredPostRequest(string postRequestUuid, CancellationToken cancellationToken)
    {
        try {
            await _requestsRepo.Remove(postRequestUuid, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to remove stored post request '{Id}'", postRequestUuid);
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

    private SendMessageRequestEntry CreateStoredSendRequest(
        string uuid,
        Moment now,
        SendMessageRequest cmd,
        FilesUpload? filesUpload)
    {
        var attachEntries = filesUpload?.CreateAttachFileRequests() ?? [];
        var clientId = !cmd.LocalId.HasValue ? Guid.NewGuid().ToString() : "";
        var entry = new SendMessageRequestEntry {
            Uuid = uuid,
            Now = now,
            ChatId = cmd.ChatId,
            LocalId = cmd.LocalId,
            Text = cmd.Text,
            RepliedEntryLid = cmd.RepliedEntryLid,
            AttachFileRequests = attachEntries,
            ClientId = clientId,
            AfterSendMessageHandlerKey = cmd.AfterSendMessageHandler?.Key ?? "",
            AfterSendMessageHandlerArgs = cmd.AfterSendMessageHandler?.Args ?? "",
        };
        return entry;
    }
}
