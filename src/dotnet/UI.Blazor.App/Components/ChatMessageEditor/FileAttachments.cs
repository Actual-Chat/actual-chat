using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class FileAttachments : UIServiceBase<AppUIHub>
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.createFromFileId";

    private AttachmentsController AttachmentsController { get; }
    public ChatId ChatId { get; }

    public FileAttachments(AppUIHub hub, ChatId chatId) : base(hub)
    {
        AttachmentsController = Hub.Services.GetRequiredService<AttachmentsController>();
        ChatId = chatId;
    }

    public async Task<bool> TryAddWebFileAttachment(AttachmentListHolder holder, int id, string fileName, string fileType, long size)
    {
        var list = holder.Attachments;
        if (CheckCanAdd(list, size) is { } e) {
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
        if (CheckCanAdd(list, fileInfo.Length) is { } e) {
            UICommander.ShowError(e);
            return false;
        }

        var fileProvider = fileInfo.FileProvider;
        fileProvider.Initialize(Hub.Services);
        return await AddFileAttachment(list, fileProvider, fileInfo.FileType);
    }

    private Exception? CheckCanAdd(AttachmentList list, long length)
    {
        if (length > Constants.Attachments.FileSizeLimit)
            return AttachmentList.FileTooBigError();

        if (list.Count >= Constants.Attachments.FileCountLimit)
            return StandardError.Constraint("Too many files. Max allowed number is 10.");

        return null;
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

    private async Task<bool> AddFileAttachment(AttachmentList list, IFileProvider fileProvider, string fileType)
    {
        var previewUrl = await fileProvider.GetPreviewUrl();
        var attachment = new Attachment(
            previewUrl,
            fileProvider.FileName,
            fileType,
            new AttachFileRequest(fileProvider));
        await AttachmentsController.AddAttachment(list, attachment);
        await AttachmentsController.InitUpload(list, attachment.Id, ChatId);
        await AttachmentsController.ResumeUpload(list, attachment.Id);
        return true;
    }

    private struct CreateWebFileProviderResult
    {
        public string PreviewUrl { get; init; }
        public IJSObjectReference FileProvider { get; init; }
    }
}
