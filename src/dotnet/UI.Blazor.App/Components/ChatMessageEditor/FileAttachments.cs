using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class FileAttachments : UIServiceBase<AppUIHub>
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.createFromFileId";

    private readonly ConcurrentDictionary<AttachmentId, ImageProcessing> _imageProcessings = new();

    private AttachmentsController AttachmentsController { get; }
    private AttachmentsState AttachmentsState { get; }
    private FilePreviews FilePreviews { get; }
    private ImageAttachmentProcessor ImageAttachmentProcessor { get; }
    private UploadSessions UploadSessions => Hub.UploadSessions;
    public ChatId ChatId { get; }

    public FileAttachments(AppUIHub hub, ChatId chatId) : base(hub)
    {
        AttachmentsController = Hub.Services.GetRequiredService<AttachmentsController>();
        AttachmentsState = Hub.AttachmentsState;
        FilePreviews = Hub.Services.GetRequiredService<FilePreviews>();
        ImageAttachmentProcessor = Hub.Services.GetRequiredService<ImageAttachmentProcessor>();
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

    public async Task SetImageQuality(AttachmentList list, ImageQualityPreset preset)
    {
        if (list.ImageQuality == preset)
            return;

        list.SetImageQuality(preset);
        var reprocessTasks = list.Items
            .Where(a => a.Source is not null)
            .Select(a => Reprocess(list, a.Id, preset))
            .ToList();
        await Task.WhenAll(reprocessTasks);
    }

    public async Task WhenImagesProcessed(AttachmentList list)
    {
        // An attachment added while Send is waiting starts its own processing, so one pass isn't enough
        while (true) {
            var tasks = new List<Task>();
            foreach (var item in list.Items) {
                if (_imageProcessings.TryGetValue(item.Id, out var processing) && !processing.Task.IsCompleted)
                    tasks.Add(processing.Task);
            }
            if (tasks.Count == 0)
                return;

            await Task.WhenAll(tasks).SilentAwait();
        }
    }

    // Private methods

    private async Task<bool> TryAddWebFileAttachment(
        AttachmentList list,
        int id,
        string fileName,
        string fileType,
        long size)
    {
        // A browser knows a File's size upfront, and 0 there is what a paste of an image whose
        // clipboard data is already gone yields; the server can't create an upload for it either.
        if (size <= 0) {
            UICommander.ShowError(StandardError.Upload.FileEmpty());
            return false;
        }
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
        if (await TryCreateAttachment(webFileProvider) is not { } attachment)
            return false;

        await AddAttachment(list, attachment);
        return true;
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

    private async Task AddAttachment(AttachmentList list, Attachment attachment)
    {
        if (!attachment.IsProcessableImage) {
            list.Add(await StartUpload(attachment, list.MediaScope));
            return;
        }

        var fileProvider = attachment.FileProvider!;
        attachment.Cleanups.RemoveByKind(AttachmentCleanupKind.File);
        attachment.Cleanups.Add(AttachmentCleanupFactory.ForSourceFile(fileProvider));
        attachment = attachment with {
            Source = new AttachmentSource(
                fileProvider, attachment.FileName, attachment.FileType, attachment.Length, attachment.Size),
            IsProcessing = true,
        };
        SetSourcePreview(attachment);
        list.Add(attachment);
        _ = StartImageProcessing(list, attachment.Id, list.ImageQuality, null);
    }

    private async Task Reprocess(AttachmentList list, AttachmentId id, ImageQualityPreset preset)
    {
        // The replacement entry is registered before the cancelled one is awaited, so
        // WhenImagesProcessed never sees this attachment as idle mid-reprocess
        Task? previousTask = null;
        if (_imageProcessings.TryRemove(id, out var previous)) {
            previous.CancellationTokenSource.CancelAndDisposeSilently();
            previousTask = previous.Task;
        }
        await StartImageProcessing(list, id, preset, previousTask);
    }

    private Task StartImageProcessing(
        AttachmentList list,
        AttachmentId id,
        ImageQualityPreset preset,
        Task? previousTask)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var task = ProcessImageAndUpload(list, id, preset, previousTask, cancellationTokenSource.Token);
        var processing = new ImageProcessing(cancellationTokenSource, task);
        _imageProcessings[id] = processing;
        _ = task.ContinueWith(
            _ => {
                // Reprocess removes (and disposes) a superseded entry itself
                if (_imageProcessings.TryRemove(new KeyValuePair<AttachmentId, ImageProcessing>(id, processing)))
                    cancellationTokenSource.Dispose();
            },
            TaskScheduler.Default);
        return task;
    }

    private async Task ProcessImageAndUpload(
        AttachmentList list,
        AttachmentId id,
        ImageQualityPreset preset,
        Task? previousTask,
        CancellationToken cancellationToken)
    {
        try {
            if (previousTask is not null)
                await previousTask.SilentAwait();
            if (list.Items.FirstOrDefault(a => a.Id == id) is not { Source: { } source } pending)
                return;

            if (previousTask is not null)
                ReleaseForReprocessing(list, pending, source);

            var result = await ImageAttachmentProcessor
                .Process(source.FileProvider, source.Size, preset, cancellationToken);
            var current = list.Items.FirstOrDefault(a => a.Id == id);
            if (cancellationToken.IsCancellationRequested || current is not { } attachment) {
                // Only a processed file is ours to delete; a null provider means the source itself
                if (result?.FileProvider is { } processedFileProvider)
                    await processedFileProvider.ClearForRemoving();
                return;
            }

            var processed = result?.FileProvider is null
                ? attachment with {
                    FileProvider = source.FileProvider,
                    FileName = source.FileName,
                    FileType = source.FileType,
                    Length = source.Length,
                    Size = source.Size,
                    SizeEstimate = result?.SizeEstimate ?? attachment.SizeEstimate,
                }
                : attachment with {
                    FileProvider = result.FileProvider,
                    FileName = result.FileProvider.Metadata.FileName,
                    FileType = result.FileProvider.Metadata.FileType,
                    Length = result.FileProvider.Metadata.Length,
                    Size = result.Size,
                    SizeEstimate = result.SizeEstimate ?? attachment.SizeEstimate,
                };
            processed = processed with { IsProcessing = false, SelectedQuality = preset };
            try {
                processed = await StartUpload(processed, list.MediaScope);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                // Without a session the attachment can't be posted, so it leaves the list
                Log.LogError(e, "Failed to start the upload of attachment '{AttachmentId}'", id);
                UICommander.ShowError(StandardError.Constraint("Failed to add file attachment."));
                if (ReferenceEquals(list.Items.FirstOrDefault(a => a.Id == id), attachment))
                    await list.Remove(attachment);
                return;
            }

            if (!ReferenceEquals(list.Items.FirstOrDefault(a => a.Id == id), attachment)) {
                // Removed while its upload session was being created: release what StartUpload registered
                processed.Cleanups.RemoveByKind(AttachmentCleanupKind.UploadSession);
                AttachmentsState.Unregister(id);
                var isSourceUpload = ReferenceEquals(processed.FileProvider, source.FileProvider);
                UploadSessions.ReleaseReference(processed.UploadSessionId, mustKeepFile: isSourceUpload);
                return;
            }

            list.Replace(attachment, processed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // A newer preset took over
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to process or upload attachment '{AttachmentId}'", id);
            UICommander.ShowError(StandardError.Constraint("Failed to add file attachment."));
        }
    }

    private void ReleaseForReprocessing(AttachmentList list, Attachment attachment, AttachmentSource source)
    {
        if (!attachment.UploadSessionId.IsNullOrEmpty()) {
            AttachmentsState.Unregister(attachment.Id);
            attachment.Cleanups.RemoveByKind(AttachmentCleanupKind.UploadSession);
            // The source is processed again right after, so its file must survive the released session
            var isSourceUpload = ReferenceEquals(attachment.FileProvider, source.FileProvider);
            UploadSessions.ReleaseReference(attachment.UploadSessionId, mustKeepFile: isSourceUpload);
        }
        var reset = attachment with { UploadSessionId = "", IsProcessing = true };
        list.Replace(attachment, reset);
        SetSourcePreview(reset);
    }

    private async Task<Attachment> StartUpload(Attachment attachment, string mediaScope)
    {
        attachment = await AttachmentsController.InitUploadSession(attachment, mediaScope);
        AttachmentsState.Register(attachment);
        AttachmentsController.ResumeUpload(attachment);
        return attachment;
    }

    private void SetSourcePreview(Attachment attachment)
    {
        if (attachment is SourceAttachment sourceAttachment)
            AttachmentsState.SetPreview(attachment.Id, AttachmentPreview.From(sourceAttachment.Preview));
    }

    // Nested types

    private struct CreateWebFileProviderResult
    {
        public string PreviewUrl { get; init; }
        public IJSObjectReference FileProvider { get; init; }
    }

    private sealed record ImageProcessing(CancellationTokenSource CancellationTokenSource, Task Task);
}
