using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class FileAttachments : UIServiceBase<AppUIHub>
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.createFromFileId";

    private AttachmentsController AttachmentsController { get; }
    private AttachmentsState AttachmentsState { get; }
    private FilePreviews FilePreviews { get; }
    public ChatId ChatId { get; }

    public FileAttachments(AppUIHub hub, ChatId chatId) : base(hub)
    {
        AttachmentsController = Hub.Services.GetRequiredService<AttachmentsController>();
        AttachmentsState = Hub.AttachmentsState;
        FilePreviews = Hub.Services.GetRequiredService<FilePreviews>();
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
        // The files are created concurrently and added in pick order: a native gallery pick loads
        // every file in the background, and a preview may take seconds per file (macOS generates
        // it from the loaded file), so a serial loop would show each item only after the previous one.
        // TODO: why it started adding slowly only on macos? investigate it
        var createTasks = new List<Task<Attachment?>>();
        foreach (var fileInfo in fileInfos) {
            if (CheckCanAdd(list, fileInfo.FileProvider.Metadata.Length, createTasks.Count) is { } e) {
                UICommander.ShowError(e);
                continue;
            }

            var fileProvider = fileInfo.FileProvider;
            fileProvider.Initialize(Hub.Services);
            createTasks.Add(TryCreateAttachment(fileProvider));
        }

        var hasAdded = false;
        foreach (var createTask in createTasks) {
            if (await createTask is not { } attachment)
                continue;

            await AddAttachment(list, attachment);
            if (!hasAdded)
                _ = TuneUI.Play(Tune.ChangeAttachments);
            hasAdded = true;
        }
        return hasAdded;
    }

    private async Task<bool> TryAddWebFileAttachment(AttachmentList list, int id, string fileName, string fileType, long size)
    {
        if (CheckCanAdd(list, size) is { } e) {
            UICommander.ShowError(e);
            return false;
        }
        // Browser's File System Access API may return empty MIME type for some files (e.g., MOV).
        // Fall back to detecting from file extension.
        if (fileType.IsNullOrEmpty())
            fileType = MediaMimeTypes.GetMimeType(fileName);
        var webFileProvider = await CreateWebFileProvider(id, fileName, fileType, size);
        if (webFileProvider is null)
            return false;

        webFileProvider.Initialize(Hub.Services);
        return await TryAddFileAttachment(list, webFileProvider);
    }

    private static Exception? CheckCanAdd(AttachmentList list, long length, int pendingCount = 0)
    {
        if (length > Constants.Attachments.FileSizeLimit)
            return StandardError.Upload.FileTooBig(Constants.Attachments.FileSizeLimit);

        if (list.Count + pendingCount >= Constants.Attachments.FileCountLimit)
            return StandardError.Upload.TooManyFiles(Constants.Attachments.FileCountLimit);

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
        if (await TryCreateAttachment(fileProvider) is not { } attachment)
            return false;

        await AddAttachment(list, attachment);
        return true;
    }

    private async Task<Attachment?> TryCreateAttachment(IFileProvider fileProvider)
    {
        try {
            return await CreateAttachment(fileProvider);
        }
        catch (Exception ex) {
            await AttachmentCleanupFactory.ForFile(fileProvider)
                .Cleanup.Invoke()
                .WithErrorLog(Log, "Failed to cleanup file provider")
                .SilentAwait();
            Log.LogError(ex, "Failed to add file attachment");
            UICommander.ShowError(StandardError.Constraint("Failed to add file attachment."));
            return null;
        }
    }

    private async Task AddAttachment(AttachmentList list, Attachment attachment)
    {
        // NOTE: Start upload immediately after adding attachments.
        attachment = await AttachmentsController.InitUploadSession(attachment, list.MediaScope);
        AttachmentsState.Register(attachment);
        AttachmentsController.ResumeUpload(attachment);
        list.Add(attachment);
    }

    private async Task<Attachment> CreateAttachment(IFileProvider fileProvider)
    {
        var fileMetadata = fileProvider.Metadata;
        var preview = await FilePreviews.Get(fileProvider, fileMetadata.FileType, Hub.StopToken);
        var attachment = new SourceAttachment(
            fileMetadata.FileName,
            fileMetadata.FileType,
            fileMetadata.Length,
            preview) {
            FileProvider = fileProvider,
            DurationMs = preview?.DurationMs ?? 0,
        };
        attachment.Cleanups.Add(AttachmentCleanupFactory.ForFile(fileProvider));
        return attachment;
    }

    private static bool IsImagePreviewUrl(string? previewUrl)
    {
        if (previewUrl.IsNullOrEmpty())
            return false;

        var decodedUrl = Uri.UnescapeDataString(previewUrl);
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        return imageExtensions.Any(ext => decodedUrl.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private struct CreateWebFileProviderResult
    {
        public string PreviewUrl { get; init; }
        public IJSObjectReference FileProvider { get; init; }
    }
}
