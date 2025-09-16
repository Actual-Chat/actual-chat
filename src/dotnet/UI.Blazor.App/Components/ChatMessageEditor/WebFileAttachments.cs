using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class WebFileAttachments(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private static readonly string JSPrepareMethod = $"{BlazorUIAppModule.ImportName}.WebFileAttachments.create";

    private UploadSessions UploadSessions => Hub.UploadSessions;

    public async Task<bool> TryAddWebFileAttachment(AttachmentListHolder holder, AttachmentWebFilePickerBackend.FileInfo fileInfo, ChatId chatId)
    {
        var list = holder.Attachments;
        if (list.CheckCanAdd(fileInfo.Length) is { } e) {
            UICommander.ShowError(e);
            return false;
        }
        WebFileProviderInternal? webFileProviderInternal;
        var fileUploaderBackend = new FileUploaderBackend();
        try {
            var webFileAttachment = await JS
                .InvokeAsync<CreateWebFileAttachmentResult>(JSPrepareMethod, fileInfo.Id, chatId.Value, fileUploaderBackend.BlazorRef)
                .ConfigureAwait(true); // Continue on Blazor context.
            webFileProviderInternal = new WebFileProviderInternal(
                webFileAttachment.FileProvider,
                fileUploaderBackend,
                true);
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to create file provider");
            fileUploaderBackend.Dispose();
            return false;
        }

        var webFileProvider = new WebFileProvider {
            FileName = fileInfo.FileName,
            ChatId = chatId,
            WebFileProviderInternal = webFileProviderInternal,
        };
        var attachment = new Attachment(Guid.NewGuid().ToString(),
            fileInfo.FileName,
            fileInfo.FileName,
            fileInfo.FileType) {
            FileProvider = webFileProvider
        };
        _ = Dispatcher.InvokeAsync(() => {
            list.Add(attachment);
        });

        // TODO(DF): review these code correctness.
        string uploadSessionId = "";
        try {
            var uploadSession = await UploadSessions.CreateSession(chatId, webFileProvider);
            uploadSessionId = uploadSession.SessionId;
            list.UpdateAttachment(attachment.Id, a => a with { UploadSessionId = uploadSession.SessionId });
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
            fileUploaderBackend.Dispose();
        }
        if (!uploadSessionId.IsNullOrEmpty())
            await UploadSessions.CancelSession(uploadSessionId);
        return false;
    }

    public struct CreateWebFileAttachmentResult
    {
        public string Id { get; init; }
        public string PreviewUrl { get; init; }
        public IJSObjectReference FileProvider { get; init; }
    }
}
