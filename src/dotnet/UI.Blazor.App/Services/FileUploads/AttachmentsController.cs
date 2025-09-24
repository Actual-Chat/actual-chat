namespace ActualChat.UI.Blazor.App.Services;

public class AttachmentsController(UploadSessions uploadSessions, Dispatcher dispatcher, ILogger<AttachmentsController> log)
{
    private Dispatcher Dispatcher => dispatcher;
    private UploadSessions UploadSessions => uploadSessions;
    private ILogger Log => log;

    public Task AddAttachment(AttachmentList list, Attachment attachment)
        => Dispatcher.InvokeAsync(() => {
            attachment.RestartUploadRequested += OnRestartUploadRequested;
            attachment.RemovedFromList += OnRemovedFromList;
            list.Add(attachment);
        });

    public async Task<UploadSession?> InitUpload(AttachmentList list, string attachmentId, ChatId chatId)
    {
        var attachment = DemandAttachment(list, attachmentId);
        if (!attachment.UploadSessionId.IsNullOrEmpty())
            throw new InvalidOperationException("Upload session already assigned");

        if (attachment.Request is not AttachFileRequest attachFileRequest)
            throw new InvalidOperationException("Can't initialize upload for non-file attachment");

        var fileProvider = attachFileRequest.FileProvider;
        if (fileProvider == null)
            throw new InvalidOperationException("File provider not assigned");

        try {
            var uploadSession = await UploadSessions.CreateSession(chatId, fileProvider).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => {
                list.UpdateAttachment(attachmentId, a => a with {
                    Request = new UploadSessionAttachRequest(UploadSessions, uploadSession.SessionId, a.Request),
                });
            }).ConfigureAwait(false);
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
        await uploadSessions.ResetSession(attachment.UploadSessionId).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() => {
            list.UpdateAttachment(attachment.Id, a1 => a1 with {
                Failed = false,
                Progress = 0,
                MediaId = null,
                ThumbnailMediaId = null,
            });
        }).ConfigureAwait(false);
    }

    private static Attachment DemandAttachment(AttachmentList list, string attachmentId)
    {
        var attachment = list.Items.FirstOrDefault(a => OrdinalEquals(a.Id, attachmentId));
        if (attachment == null)
            throw new InvalidOperationException("Attachment not found");
        return attachment;
    }

    private Task OnRestartUploadRequested(AttachmentList list, Attachment attachment)
        => BackgroundTask.Run(async () => {
            await RestartUpload(list, attachment.Id).ConfigureAwait(false);
        }, Log, "RestartUpload").SuppressExceptions();

    private Task OnRemovedFromList(AttachmentList list, Attachment a)
        => BackgroundTask.Run(async () => {
            await CleanupAttachmentResources(a).ConfigureAwait(false);
        }, Log, "RemovedFromList").SuppressExceptions();

    private Task CleanupAttachmentResources(Attachment a)
        => a.Request.CleanupForRemoving();
}
