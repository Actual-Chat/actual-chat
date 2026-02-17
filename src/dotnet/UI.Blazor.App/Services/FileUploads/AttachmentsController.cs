using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class AttachmentsController(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IAttachmentListEventsListener
{
    private UploadSessions UploadSessions => Hub.UploadSessions;
    private AttachmentsState AttachmentsState => Hub.AttachmentsState;

    public async Task<Attachment> InitUploadSession(Attachment attachment)
    {
        if (!attachment.UploadSessionId.IsNullOrEmpty())
            throw new InvalidOperationException("Upload session already assigned");

        if (attachment.FileProvider is not { } fileProvider)
            throw new InvalidOperationException(
                $"Can't initialize upload for attachment '{attachment.Id}'. No file provider assigned.");

        try {
            var uploadSessionId = await UploadSessions.CreateSession(fileProvider, attachment.GetMetadataForUploadSession()).ConfigureAwait(false);
            attachment = attachment with {
                UploadSessionId = uploadSessionId,
            };
            // UploadSession cleanup will handle file cleanup. So just replace it.
            UploadSessions.AddReference(uploadSessionId);
            attachment.Cleanups.RemoveByKind(AttachmentCleanupKind.File);
            attachment.Cleanups.Add(AttachmentCleanupFactory.ForUploadSession(UploadSessions, uploadSessionId));
            return attachment;
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to create/resume upload session");
            throw;
        }
    }

    public Task ResumeUpload(Attachment attachment)
    {
        var uploadSessionId = attachment.DemandUploadSessionId();
        try {
            UploadSessions.Resume(uploadSessionId);
        }
        catch (Exception ex) {
            Log.LogWarning(ex, "Failed to resume upload session '{SessionId}'", uploadSessionId);
        }
        return Task.CompletedTask;
    }

    public async Task RestartUpload(Attachment attachment)
    {
        var progress = await AttachmentsState.GetProgress(attachment.Id, default).ConfigureAwait(false);
        if (progress.IsFailed)
            throw new InvalidOperationException("Can't restart. Upload is not failed");
        var previewState = await AttachmentsState.GetPreview(attachment.Id, default).ConfigureAwait(false);
        if (previewState.State is PreviewAccessState.NoFileAccess)
            throw new InvalidOperationException("Can't restart. No access to file");

        await ResumeUpload(attachment).ConfigureAwait(false);
    }

    Task IAttachmentListEventsListener.AttachmentsRemoved(AttachmentList list, Attachment[] attachments)
    {
        foreach (var a in attachments)
            AttachmentsState.Unregister(a.Id);
        _ = TuneUI.Play(Tune.ChangeAttachments);
        return BackgroundTask.Run(async () => {
                    foreach (var a in attachments)
                        await CleanupAttachmentResources(a).ConfigureAwait(false);
                },
                Log,
                "RemovedFromList")
            .SuppressExceptions();
    }

    Task IAttachmentListEventsListener.RestartUploadRequested(
        AttachmentList list,
        Attachment attachment)
    {
        _ = TuneUI.Play(Tune.ChangeAttachments);
        return BackgroundTask.Run(async () => {
                    await RestartUpload(attachment).ConfigureAwait(false);
                },
                Log,
                "RestartUpload")
            .SuppressExceptions();
    }

    private static async Task CleanupAttachmentResources(Attachment a)
    {
        foreach (var cleanup in a.Cleanups.Items)
            await cleanup.Cleanup().ConfigureAwait(false);
    }
}
