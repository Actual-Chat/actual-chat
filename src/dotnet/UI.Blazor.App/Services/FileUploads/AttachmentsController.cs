using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class AttachmentsController(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IAttachmentListEventsListener
{
    private UploadSessions UploadSessions => Hub.UploadSessions;

    public Task AddAttachment(AttachmentList list, Attachment attachment)
        => Dispatcher.InvokeAsync(() => {
            list.Subscribe(this);
            list.Add(attachment);
        });

    public async Task<UploadSession?> InitUpload(AttachmentList list, string attachmentId, ChatId chatId)
    {
        var attachment = DemandAttachment(list, attachmentId);
        if (!attachment.UploadSessionId.IsNullOrEmpty())
            throw new InvalidOperationException("Upload session already assigned");

        if (attachment.FileProvider is not { } fileProvider)
            throw new InvalidOperationException(
                $"Can't initialize upload for attachment '{attachmentId}'. No file provider assigned.");

        try {
            var uploadSession = await UploadSessions.CreateSession(chatId, fileProvider).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => {
                    list.UpdateAttachment(attachmentId,
                        a => {
                            var a1 = a with {
                                UploadSessionId = uploadSession.SessionId,
                            };
                            // UploadSession cleanup will handle file cleanup. So just replace it.
                            a1.Cleanups.RemoveByKind(AttachmentCleanupKind.File);
                            a1.Cleanups.Add(
                                AttachmentCleanupFactory.ForUploadSession(UploadSessions, uploadSession.SessionId));
                            return a1;
                        });
                })
                .ConfigureAwait(false);
            return uploadSession;
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to create/resume upload session");
            return null;
        }
    }

    public async Task ResumeUpload(AttachmentList list, string attachmentId)
    {
        var attachment = DemandAttachment(list, attachmentId);
        var uploadSession = await UploadSessions.ResumeSession(attachment.UploadSessionId).ConfigureAwait(false);
        AttachmentExt.ObserveUploadProgress(
            uploadSession.ProgressTracker,
            updater => {
                _ = Dispatcher.InvokeAsync(() => {
                    list.UpdateAttachment(attachment.Id, updater);
                });
            });
    }

    public async Task RestartUpload(AttachmentList list, string attachmentId)
    {
        var attachment = DemandAttachment(list, attachmentId);
        if (!attachment.Failed)
            throw new InvalidOperationException("Can't restart. Upload is not failed");
        if (attachment.NoAccess)
            throw new InvalidOperationException("Can't restart. No access to file");
        if (attachment.UploadSessionId.IsNullOrEmpty())
            throw new InvalidOperationException("Upload is not initialized yet.");

        await ResetUpload(list, attachment).ConfigureAwait(false);
        await ResumeUpload(list, attachmentId).ConfigureAwait(false);
    }

    private async Task ResetUpload(AttachmentList list, Attachment attachment)
    {
        await UploadSessions.ResetSession(attachment.UploadSessionId).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() => {
                list.UpdateAttachment(attachment.Id,
                    a1 => a1 with {
                        Failed = false,
                        Progress = 0,
                        MediaId = null,
                        ThumbnailMediaId = null,
                    });
            })
            .ConfigureAwait(false);
    }

    private static Attachment DemandAttachment(AttachmentList list, string attachmentId)
    {
        var attachment = list.Items.FirstOrDefault(a => OrdinalEquals(a.Id, attachmentId));
        return attachment ?? throw new InvalidOperationException("Attachment not found");
    }

    Task IAttachmentListEventsListener.AttachmentsRemoved(AttachmentList list, Attachment[] attachments)
    {
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
                    await RestartUpload(list, attachment.Id).ConfigureAwait(false);
                },
                Log,
                "RestartUpload")
            .SuppressExceptions();
    }

    private async Task CleanupAttachmentResources(Attachment a)
    {
        foreach (var cleanup in a.Cleanups.Items)
            await cleanup.Cleanup().ConfigureAwait(false);
    }
}
