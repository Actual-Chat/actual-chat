using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class FileAttachments : UIServiceBase<AppUIHub>
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.createFromFileId";
    private static readonly string JSGetDimensions = $"{BlazorUIAppModule.ImportName}.MediaFileDimensions.getDimensions";


    private AttachmentsController AttachmentsController { get; }
    public ChatId ChatId { get; }

    public FileAttachments(AppUIHub hub, ChatId chatId) : base(hub)
    {
        AttachmentsController = Hub.Services.GetRequiredService<AttachmentsController>();
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
        var webFileProvider = await CreateWebFileProvider(id, fileName);
        if (webFileProvider is null)
            return false;

        webFileProvider.Initialize(Hub.Services);
        return await AddFileAttachment(list, webFileProvider, fileType);
    }

    private async Task<bool> TryAddFileAttachment(AttachmentList list, AttachFileInfo fileInfo)
    {
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

        var dimensions = await GetFileDimensionsAsync(previewUrl, fileType);
        var width = dimensions.width;
        var height = dimensions.height;

        var attachment = new Attachment(
            previewUrl,
            fileProvider.FileName,
            fileType,
            width,
            height) {
            FileProvider = fileProvider,
        };
        attachment.Cleanups.Add(AttachmentCleanupFactory.ForFile(fileProvider));
        await AttachmentsController.AddAttachment(list, attachment);
        // NOTE: Do not start upload immediately after adding attachments.
        // await AttachmentsController.InitUpload(list, attachment.Id, ChatId);
        // await AttachmentsController.ResumeUpload(list, attachment.Id);
        return true;
    }

    private async Task<(int width, int height)> GetFileDimensionsAsync(string previewUrl, string mimeType) {
        var dimensions = await JS.InvokeAsync<MediaDimensions?>(JSGetDimensions, previewUrl, mimeType).ConfigureAwait(false);
        if (dimensions is null)
            return (0, 0);

        return (dimensions.Width, dimensions.Height);
    }

    private struct CreateWebFileProviderResult
    {
        public string PreviewUrl { get; init; }
        public IJSObjectReference FileProvider { get; init; }
    }
}
