using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class FileAttachments(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.createFromFileId";

    private UploadSessions UploadSessions => Hub.UploadSessions;

    public async Task<bool> TryAddWebFileAttachment(AttachmentListHolder holder, int id, string fileName, string fileType, long size)
    {
        var list = holder.Attachments;
        if (list.CheckCanAdd(size) is { } e) {
            UICommander.ShowError(e);
            return false;
        }
        var webFileProvider = await CreateWebFileProvider(id, fileName);
        if (webFileProvider is null)
            return false;

        webFileProvider.Initialize(Hub.Services);
        return await AddFileAttachment(list, webFileProvider, fileType);
    }

    public async Task<bool> TryAddFileAttachment(AttachmentListHolder holder, AttachFileInfo fileInfo)
    {
        var list = holder.Attachments;
        if (list.CheckCanAdd(fileInfo.Length) is { } e) {
            UICommander.ShowError(e);
            return false;
        }

        var fileProvider = fileInfo.FileProvider;
        fileProvider.Initialize(Hub.Services);
        return await AddFileAttachment(list, fileProvider, fileInfo.FileType);
    }

    private async Task<bool> AddFileAttachment(AttachmentList list, IFileProvider fileProvider, string fileType)
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
            var uploadSession = await UploadSessions.CreateSession(list.ChatId, attachment.FileProvider);
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

    private async Task<WebFileProvider?> CreateWebFileProvider(int id, string fileName)
    {
        WebFileProviderInternal? webFileProviderInternal;
        try {
            var webFileAttachment = await JS
                .InvokeAsync<CreateWebFileProviderResult>(JSCreateMethod, id)
                .ConfigureAwait(true); // Continue on Blazor context.
            webFileProviderInternal = new WebFileProviderInternal(
                webFileAttachment.FileProvider,
                webFileAttachment.PreviewUrl,
                true);
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to create file provider");
            return null;
        }

        var webFileProvider = new WebFileProvider {
            FileName = fileName,
            WebFileProviderInternal = webFileProviderInternal,
        };
        return webFileProvider;
    }

    private struct CreateWebFileProviderResult
    {
        public string PreviewUrl { get; init; }
        public IJSObjectReference FileProvider { get; init; }
    }
}
