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
            var uploadSession = await UploadSessions.CreateSession(fileProvider, attachment.GetMetadataForUploadSession()).ConfigureAwait(false);
            attachment = attachment with {
                UploadSessionId = uploadSession.SessionId,
            };
            // UploadSession cleanup will handle file cleanup. So just replace it.
            await UploadSessions.AddReference(uploadSession.SessionId).ConfigureAwait(false);
            attachment.Cleanups.RemoveByKind(AttachmentCleanupKind.File);
            attachment.Cleanups.Add(AttachmentCleanupFactory.ForUploadSession(UploadSessions, uploadSession.SessionId));
            return attachment;
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to create/resume upload session");
            throw;
        }
    }

    public async Task ResumeUpload(Attachment attachment)
    {
        var uploadSessionId = attachment.DemandUploadSessionId();
        UploadSession uploadSession;
        try {
            uploadSession = await UploadSessions.ResumeSession(uploadSessionId).ConfigureAwait(false);
        }
        catch (Exception ex) {
            Log.LogWarning(ex, "Failed to resume upload session '{SessionId}'", uploadSessionId);
            AttachmentsState.SetFailureState(attachment.Id, FailureState.Failed);
        }
    }

    public async Task RestartUpload(Attachment attachment)
    {
        var failureState = await AttachmentsState.GetFailureState(attachment.Id, default).ConfigureAwait(false);
        if (failureState is not FailureState.Failed)
            throw new InvalidOperationException("Can't restart. Upload is not failed");
        var previewState = await AttachmentsState.GetPreview(attachment.Id, default).ConfigureAwait(false);
        if (previewState.State is PreviewAccessState.NoFileAccess)
            throw new InvalidOperationException("Can't restart. No access to file");

        await ResetUpload(attachment).ConfigureAwait(false);
        await ResumeUpload(attachment).ConfigureAwait(false);
    }

    private async Task ResetUpload(Attachment attachment)
    {
        var uploadSessionId = attachment.DemandUploadSessionId();
        await UploadSessions.ResetSession(uploadSessionId).ConfigureAwait(false);
        AttachmentsState.SetFailureState(attachment.Id, FailureState.Restarting);
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
