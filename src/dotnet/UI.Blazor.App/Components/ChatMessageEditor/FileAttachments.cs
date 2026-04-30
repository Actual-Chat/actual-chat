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

    private static Exception? CheckCanAdd(AttachmentList list, long length)
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
        // Defer upload for resizable images to allow quality selection.
        if (attachment.IsResizableImage) {
            attachment = attachment with { IsUploadPending = true, OriginalLength = attachment.Length };
            if (attachment is SourceAttachment source)
                AttachmentsState.SetPreview(attachment.Id, AttachmentPreview.From(source.Preview));
            list.Add(attachment);
            _ = EstimateAndUpdateLength(list, attachment);
            return true;
        }
        // NOTE: Start upload immediately after adding non-image attachments.
        attachment = await StartUpload(attachment, list);
        list.Add(attachment);
        return true;
    }

    public async Task ConfirmImageQuality(AttachmentList list, Attachment attachment, ImageQualityPreset preset)
    {
        if (!attachment.IsUploadPending)
            return;

        var maxDimension = (int)preset;
        if (attachment.FileProvider is WebFileProvider webFileProvider
            && (attachment.Width > maxDimension || attachment.Height > maxDimension)) {
            var result = await webFileProvider.ResizeImage(maxDimension).ConfigureAwait(true);
            var newAttachment = attachment with {
                Length = result.Size,
                Size = new Size(result.Width, result.Height),
                IsUploadPending = false,
                SelectedQuality = preset,
            };
            list.Replace(attachment, newAttachment);
            attachment = newAttachment;
        }
        else {
            var newAttachment = attachment with {
                IsUploadPending = false,
                SelectedQuality = preset,
            };
            list.Replace(attachment, newAttachment);
            attachment = newAttachment;
        }

        attachment = await StartUpload(attachment, list);
        list.Replace(list.Items.First(a => a.Id == attachment.Id), attachment);
    }

    public async Task ApplyQualityAndStartUploads(AttachmentList list)
    {
        foreach (var a in list.Items.Where(a => a.IsUploadPending).ToList())
            await ConfirmImageQuality(list, a, a.SelectedQuality);
    }

    private async Task EstimateAndUpdateLength(AttachmentList list, Attachment attachment)
    {
        if (attachment.FileProvider is not WebFileProvider webFileProvider)
            return;

        try {
            var presets = new ImageResizePreset[] {
                new((int)ImageQualityPreset.Maximum),
                new((int)ImageQualityPreset.Medium),
                new((int)ImageQualityPreset.Small),
            };
            var results = await webFileProvider.EstimateResizedSizes(presets).ConfigureAwait(true);
            var current = list.Items.FirstOrDefault(a => a.Id == attachment.Id);
            if (current is { IsUploadPending: true } && results.Length == 3) {
                var updated = current with {
                    Length = results[0].Size > 0 ? results[0].Size : current.Length,
                    EstimatedSizes = [..results],
                };
                list.Replace(current, updated);
            }
        }
        catch {
            // Estimation failed — keep original length.
        }
    }

    private async Task<Attachment> StartUpload(Attachment attachment, AttachmentList list)
    {
        attachment = await AttachmentsController.InitUploadSession(attachment, list.MediaScope);
        AttachmentsState.Register(attachment);
        AttachmentsController.ResumeUpload(attachment);
        return attachment;
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
