using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class CustomFileAttachments(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private UploadSessions UploadSessions => Hub.UploadSessions;

    public async Task<bool> TryAddCustomFileAttachment(AttachmentListHolder holder, AttachFileInfo fileInfo, ChatId chatId)
    {
        var list = holder.Attachments;
        if (list.CheckCanAdd(fileInfo.Length) is { } e) {
            UICommander.ShowError(e);
            return false;
        }

        var fileProvider = fileInfo.FileProvider;
        if (fileProvider is MauiFileProvider mauiFileProvider) {
            fileProvider = new MauiFileProvider {
                FileRef = mauiFileProvider.FileRef,
                FileType = mauiFileProvider.FileType,
                FileName = mauiFileProvider.FileName,
                ChatId = chatId,
            };
        }
        fileProvider.Initialize(Hub.Services);
        return await AddCustomFileAttachment(list, fileProvider, fileInfo.FileType, chatId);
    }

    private async Task<bool> AddCustomFileAttachment(AttachmentList list, IFileProvider fileProvider, string fileType,
        ChatId chatId)
    {
        var previewUrl = await fileProvider.GetPreviewUrl();
        var attachment = new Attachment(
            Guid.NewGuid().ToString(),
            previewUrl,
            fileProvider.FileName,
            fileType) {
            FileProvider = fileProvider,
        };
        _ = Dispatcher.InvokeAsync(() => {
            list.Add(attachment);
        });

        // TODO(DF): review these code correctness.
        string uploadSessionId = "";
        try {
            var uploadSession = await UploadSessions.CreateSession(chatId, attachment.FileProvider);
            uploadSessionId = uploadSession.SessionId;
            await Dispatcher.InvokeAsync(() => {
                list.UpdateAttachment(attachment.Id, a => a with { UploadSessionId = uploadSession.SessionId });
            });
            await UploadSessions.ResumeSession(uploadSession.SessionId);
            AttachmentExt.ObserveUploadProgress(
                uploadSession.ProgressTracker,
                updater => {
                    _ = Dispatcher.InvokeAsync(() => {
                        list.UpdateAttachment(attachment.Id, updater);
                    });
                });
            return true;
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to create/resume upload session");
        }
        if (!uploadSessionId.IsNullOrEmpty())
            await UploadSessions.CancelSession(uploadSessionId);
        return false;
    }
}
