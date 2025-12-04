using ActualChat.Media;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class FileAttachments : UIServiceBase<AppUIHub>
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.createFromFileId";

    private AttachmentsController AttachmentsController { get; }
    private VisualMediaDimensions VisualMediaDimensions { get; }
    public ChatId ChatId { get; }

    public FileAttachments(AppUIHub hub, ChatId chatId) : base(hub)
    {
        AttachmentsController = Hub.Services.GetRequiredService<AttachmentsController>();
        VisualMediaDimensions = new VisualMediaDimensions(Hub.JS, Hub.LogFor<VisualMediaDimensions>());
        ChatId = chatId;
    }

    public async Task<bool> TryAddWebFileAttachments(AttachmentList list, WebFileInfo[] fileInfos)
    {
        var hasAdded = false;
        foreach (var fileInfo in fileInfos) {
            var prevHasAdded = hasAdded;
            hasAdded = await TryAddWebFileAttachment(
                list,
                fileInfo.Id,
                fileInfo.FileName,
                fileInfo.FileType,
                fileInfo.Size);
            if (!prevHasAdded && hasAdded)
                _ = TuneUI.Play(Tune.ChangeAttachments);
        }
        return hasAdded;
    }

    public async Task<bool> TryAddFileAttachments(AttachmentList list, AttachFileInfo[] fileInfos)
    {
        var hasAdded = false;
        foreach (var fileInfo in fileInfos) {
            var prevHasAdded = hasAdded;
            hasAdded = await TryAddFileAttachment(list, fileInfo);
            if (!prevHasAdded && hasAdded)
                _ = TuneUI.Play(Tune.ChangeAttachments);
        }
        return hasAdded;
    }

    private async Task<bool> TryAddWebFileAttachment(AttachmentList list, int id, string fileName, string fileType, long size)
    {
        if (CheckCanAdd(list, size) is { } e) {
            UICommander.ShowError(e);
            return false;
        }
        var webFileProvider = await CreateWebFileProvider(id, fileName, fileType, size);
        if (webFileProvider is null)
            return false;

        webFileProvider.Initialize(Hub.Services);
        return await TryAddFileAttachment(list, webFileProvider);
    }

    private async Task<bool> TryAddFileAttachment(AttachmentList list, AttachFileInfo fileInfo)
    {
        if (CheckCanAdd(list, fileInfo.FileProvider.Metadata.Length) is { } e) {
            UICommander.ShowError(e);
            return false;
        }

        var fileProvider = fileInfo.FileProvider;
        fileProvider.Initialize(Hub.Services);
        return await TryAddFileAttachment(list, fileProvider);
    }

    private Exception? CheckCanAdd(AttachmentList list, long length)
    {
        if (length > Constants.Attachments.FileSizeLimit)
            return AttachmentList.FileTooBigError();

        if (list.Count >= Constants.Attachments.FileCountLimit)
            return StandardError.Constraint("Too many files. Max allowed number is 10.");

        return null;
    }

    private async Task<WebFileProvider?> CreateWebFileProvider(int id, string fileName, string fileType, long length)
    {
        WebFileProviderInternal? webFileProviderInternal;
        try {
            var webFileAttachment = await JS
                .InvokeAsync<CreateWebFileProviderResult>(JSCreateMethod, id)
                .ConfigureAwait(true); // Continue on Blazor context.
            webFileProviderInternal = new WebFileProviderInternal(
                webFileAttachment.FileProvider,
                webFileAttachment.PreviewUrl,
                true,
                Task.FromResult(true));
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to create file provider");
            return null;
        }
        var webFileProvider = new WebFileProvider {
            Metadata = new () {
                FileName = fileName,
                FileType = fileType,
                Length = length,
            },
            WebFileProviderInternal = webFileProviderInternal,
        };
        return webFileProvider;
    }

    private async Task<bool> TryAddFileAttachment(AttachmentList list, IFileProvider fileProvider)
    {
        Attachment attachment;
        try {
            attachment = await CreateAttachment(fileProvider);
        }
        catch (Exception ex) {
            await AttachmentCleanupFactory.ForFile(fileProvider)
                .Cleanup.Invoke()
                .WithErrorLog(Log, "Failed to cleanup file provider")
                .SilentAwait();
            Log.LogError(ex, "Failed to add file attachment");
            UICommander.ShowError("Failed to add file attachment.");
            return false;
        }

        await AttachmentsController.AddAttachment(list, attachment);
        // NOTE: Start upload immediately after adding attachments.
        await AttachmentsController.InitUpload(list, attachment.Id, ChatId);
        await AttachmentsController.ResumeUpload(list, attachment.Id);
        return true;
    }

    private async Task<Attachment> CreateAttachment(IFileProvider fileProvider)
    {
        var fileMetadata = fileProvider.Metadata;
        var previewUrl = "";
        var width = 0;
        var height = 0;
        if (MediaTypeExt.IsVisualMedia(fileMetadata.FileType)) {
            previewUrl = await fileProvider.GetPreviewUrl();
            var dimensions = await VisualMediaDimensions.GetVisualMediaDimensions(previewUrl, fileMetadata.FileType);
            width = dimensions.width;
            height = dimensions.height;
        }
        var attachment = new Attachment(
            fileMetadata.FileName,
            fileMetadata.FileType,
            fileMetadata.Length,
            width,
            height,
            Task.FromResult(true),
            Task.FromResult(previewUrl)) {
            FileProvider = fileProvider,
        };
        attachment.Cleanups.Add(AttachmentCleanupFactory.ForFile(fileProvider));
        return attachment;
    }

    private struct CreateWebFileProviderResult
    {
        public string PreviewUrl { get; init; }
        public IJSObjectReference FileProvider { get; init; }
    }
}
