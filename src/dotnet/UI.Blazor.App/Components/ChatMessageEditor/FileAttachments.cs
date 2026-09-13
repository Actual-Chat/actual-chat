using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class FileAttachments(AppUIHub hub, ChatId chatId) : UIServiceBase<AppUIHub>(hub)
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.createFromFileId";
    private static readonly HashSet<string> PreviewConversionContentTypes =
        new (StringComparer.OrdinalIgnoreCase) { "image/heif", "image/heic", "image/avif" };

    private readonly ConcurrentDictionary<AttachmentId, PendingWork> _pendingWork = new();
    // A HEIC/AVIF preview that landed while the commit pipeline was already running for the same id:
    // replacing the list item then would break the pipeline's ReferenceEquals-based removal check
    private readonly ConcurrentDictionary<AttachmentId, FilePreview?> _pendingPreviews = new();

    private AttachmentsController AttachmentsController
        => field ??= Services.GetRequiredService<AttachmentsController>();
    private AttachmentsState AttachmentsState => Hub.AttachmentsState;
    private FilePreviews FilePreviews => field ??= Services.GetRequiredService<FilePreviews>();
    private ImageAttachmentProcessor ImageAttachmentProcessor
        => field ??= Services.GetRequiredService<ImageAttachmentProcessor>();
    private UploadSessions UploadSessions => Hub.UploadSessions;
    public ChatId ChatId { get; } = chatId;

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

            AddAttachment(list, attachment);
            if (!hasAdded)
                _ = TuneUI.Play(Tune.ChangeAttachments);
            hasAdded = true;
        }
        return hasAdded;
    }

    public Task SetImageQuality(AttachmentList list, ImageQualityPreset preset)
    {
        // Returns as soon as the (re)processing is started: Send awaits it via WhenReadyToPost,
        // and the caller is a menu that must not stay open for the whole batch
        if (list.ImageQuality == preset && list.IsCommitted)
            return Task.CompletedTask;

        list.SetImageQuality(preset);
        if (!list.IsCommitted) {
            // Picking a preset is what commits the draft, and the commit starts the first pass with
            // the preset just set - so a reprocess on top of it would be a second, racing pass
            CommitDraft(list);
            return Task.CompletedTask;
        }

        foreach (var attachment in list.Items.Where(a => a.IsReEncodable).ToList())
            _ = Reprocess(list, attachment.Id, preset)
                .WithErrorLog(Log, "Failed to reprocess attachment '{AttachmentId}'", attachment.Id)
                .SilentAwait();

        return Task.CompletedTask;
    }

    public void CommitDraft(AttachmentList list)
    {
        // Idempotent: typing calls this per keystroke, and an attachment that already has its work
        // in flight or its upload session is skipped rather than started a second time
        list.Commit();
        foreach (var attachment in list.Items.ToList())
            StartWorkIfNeeded(list, attachment);
    }

    public async Task WhenReadyToPost(AttachmentList list)
    {
        // An attachment added while Send is waiting starts its own work, so one pass isn't enough
        while (true) {
            var tasks = new List<Task>();
            foreach (var item in list.Items) {
                if (_pendingWork.TryGetValue(item.Id, out var work) && !work.Task.IsCompleted)
                    tasks.Add(work.Task);
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

        AddAttachment(list, attachment);
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

    private void AddAttachment(AttachmentList list, Attachment attachment)
    {
        // Nothing is encoded or uploaded here unless the draft is already committed
        if (attachment.IsProcessableImage) {
            var fileProvider = attachment.FileProvider!;
            attachment.Cleanups.RemoveByKind(AttachmentCleanupKind.File);
            attachment.Cleanups.Add(AttachmentCleanupFactory.ForSourceFile(fileProvider));
            attachment = attachment with {
                Source = new AttachmentSource(
                    fileProvider, attachment.FileName, attachment.FileType, attachment.Length, attachment.Size),
                IsProcessing = true,
            };
        }
        SetSourcePreview(attachment);
        list.Add(attachment);
        // WebKit paints HEIC/AVIF natively, so the source preview is already fine there - converting
        // would only cost a full-resolution decode for nothing
        if (attachment.IsProcessableImage && NeedsPreviewConversion(attachment.FileType) && !Hub.BrowserInfo.IsWebKit)
            _ = ConvertPreview(list, attachment.Id, attachment.FileProvider!, attachment.Size)
                .WithErrorLog(Log, "Failed to convert HEIC/AVIF preview for attachment '{AttachmentId}'", attachment.Id)
                .SilentAwait();
        if (list.IsCommitted)
            StartWorkIfNeeded(list, attachment);
    }

    private async Task ConvertPreview(
        AttachmentList list, AttachmentId id, IFileProvider fileProvider, Size2D sourceSize)
    {
        // A Chromium WebView can't paint HEIC/AVIF, so this is the one exception to "nothing before commit" -
        // it runs in the background so the tile itself is added at zero cost, same as every other format
        var budget = ImageQualityPreset.Mpx3.GetBudget();
        var request = new ImageProcessRequest([ImageOutputSpec.Main(budget)]);
        var result = await ImageAttachmentProcessor.Process(fileProvider, sourceSize, budget, request, Hub.StopToken);
        if (result?.FileProvider is not { } previewProvider)
            return;

        // The committed pipeline may have already replaced this attachment with the real thing by now;
        // re-read it by id rather than trusting a stale reference, same as ProcessImageAndUpload does
        if (list.Items.FirstOrDefault(a => a.Id == id) is not SourceAttachment { IsProcessing: true } pending) {
            await previewProvider.ClearForRemoving();
            return;
        }

        pending.Cleanups.Add(AttachmentCleanupFactory.ForPreviewFile(previewProvider));
        var preview = await FilePreviews.Get(previewProvider, "image/jpeg", Hub.StopToken);
        if (list.Items.FirstOrDefault(a => a.Id == id) is not SourceAttachment { IsProcessing: true } current)
            return; // Removed or already replaced meanwhile; the cleanup above still owns the file

        if (_pendingWork.ContainsKey(id)) {
            // The commit pipeline is actively running for this id right now: replacing the list item
            // would break its ReferenceEquals-based "was this removed mid-flight" check on completion.
            // Stash the preview instead, so it can fold it into the attachment it eventually produces.
            _pendingPreviews[id] = preview;
            AttachmentsState.SetPreview(id, AttachmentPreview.From(preview));
            return;
        }

        var updated = current with { Preview = preview, Size = preview?.Dimensions ?? current.Size };
        list.Replace(current, updated);
        SetSourcePreview(updated);
    }

    private static bool NeedsPreviewConversion(string fileType)
        => PreviewConversionContentTypes.Contains(fileType);

    private void StartWorkIfNeeded(AttachmentList list, Attachment attachment)
    {
        var id = attachment.Id;
        if (!attachment.UploadSessionId.IsNullOrEmpty() || _pendingWork.ContainsKey(id))
            return;

        var task = attachment.IsProcessableImage
            ? StartImageProcessing(list, id, list.ImageQuality, null, isReprocess: false)
            : StartUploadOnly(list, id);
        _ = task
            .WithErrorLog(Log, "Failed to process attachment '{AttachmentId}'", id)
            .SilentAwait();
    }

    private async Task Reprocess(AttachmentList list, AttachmentId id, ImageQualityPreset preset)
    {
        // The replacement entry is registered before the cancelled one is awaited, so
        // WhenReadyToPost never sees this attachment as idle mid-reprocess
        Task? previousTask = null;
        if (_pendingWork.TryRemove(id, out var previous)) {
            previous.CancellationTokenSource.CancelAndDisposeSilently();
            previousTask = previous.Task;
        }
        await StartImageProcessing(list, id, preset, previousTask, isReprocess: true);
    }

    private Task StartImageProcessing(
        AttachmentList list,
        AttachmentId id,
        ImageQualityPreset preset,
        Task? previousTask,
        bool isReprocess)
        => StartPendingWork(
            id,
            cancellationToken
                => ProcessImageAndUpload(list, id, preset, previousTask, isReprocess, cancellationToken));

    private Task StartUploadOnly(AttachmentList list, AttachmentId id)
        => StartPendingWork(id, _ => UploadOnly(list, id));

    private Task StartPendingWork(AttachmentId id, Func<CancellationToken, Task> taskFactory)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var task = taskFactory.Invoke(cancellationTokenSource.Token);
        var work = new PendingWork(cancellationTokenSource, task);
        _pendingWork[id] = work;
        _ = task.ContinueWith(
            _ => {
                // Reprocess removes (and disposes) a superseded entry itself
                if (_pendingWork.TryRemove(new KeyValuePair<AttachmentId, PendingWork>(id, work)))
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
        bool isReprocess,
        CancellationToken cancellationToken)
    {
        try {
            if (previousTask is not null)
                await previousTask.SilentAwait();
            if (list.Items.FirstOrDefault(a => a.Id == id) is not { Source: { } source } pending)
                return;

            if (isReprocess)
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
                }
                : attachment with {
                    FileProvider = result.FileProvider,
                    FileName = result.FileProvider.Metadata.FileName,
                    FileType = result.FileProvider.Metadata.FileType,
                    Length = result.FileProvider.Metadata.Length,
                    Size = result.Size,
                };
            processed = processed with {
                IsProcessing = false,
                SelectedQuality = preset,
                Placeholder = result?.Placeholder ?? "",
                IsDeclined = result?.Declined ?? false,
            };
            // A HEIC/AVIF preview that landed while this was running is stashed rather than applied
            // directly (see ConvertPreview) - fold it in now instead of the source's broken preview
            if (_pendingPreviews.TryRemove(id, out var pendingPreview) && processed is SourceAttachment withPreview)
                processed = withPreview with { Preview = pendingPreview };
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
            // A failed encode usually happens inside Send now, and the same Post goes on to demand
            // the upload session this attachment never got - so it leaves the list, as above
            Log.LogError(e, "Failed to process or upload attachment '{AttachmentId}'", id);
            UICommander.ShowError(StandardError.Constraint("Failed to add file attachment."));
            if (list.Items.FirstOrDefault(a => a.Id == id) is { } stale && stale.UploadSessionId.IsNullOrEmpty()) {
                _pendingPreviews.TryRemove(id, out _);
                await list.Remove(stale);
            }
        }
    }

    private async Task UploadOnly(AttachmentList list, AttachmentId id)
    {
        if (list.Items.FirstOrDefault(a => a.Id == id) is not { } attachment)
            return;

        Attachment uploading;
        try {
            uploading = await StartUpload(attachment, list.MediaScope);
        }
        catch (Exception e) {
            // Without a session the attachment can't be posted, so it leaves the list
            Log.LogError(e, "Failed to start the upload of attachment '{AttachmentId}'", id);
            UICommander.ShowError(StandardError.Constraint("Failed to add file attachment."));
            if (ReferenceEquals(list.Items.FirstOrDefault(a => a.Id == id), attachment))
                await list.Remove(attachment);
            return;
        }

        if (!ReferenceEquals(list.Items.FirstOrDefault(a => a.Id == id), attachment)) {
            // Removed while its upload session was being created: release what StartUpload registered
            uploading.Cleanups.RemoveByKind(AttachmentCleanupKind.UploadSession);
            AttachmentsState.Unregister(id);
            UploadSessions.ReleaseReference(uploading.UploadSessionId);
            return;
        }

        list.Replace(attachment, uploading);
    }

    private void ReleaseForReprocessing(AttachmentList list, Attachment attachment, AttachmentSource source)
    {
        if (!attachment.UploadSessionId.IsNullOrEmpty()) {
            AttachmentsState.Unregister(attachment.Id);
            attachment.Cleanups.RemoveByKind(AttachmentCleanupKind.UploadSession);
            // The source is processed again right after, so its file must survive the released session
            var isSourceUpload = ReferenceEquals(attachment.FileProvider, source.FileProvider);
            UploadSessions.ReleaseReference(attachment.UploadSessionId, mustKeepFile: isSourceUpload);
            if (isSourceUpload) {
                // InitUploadSession dropped this cleanup when the session took the source over;
                // with the session gone, the source needs its owner back
                attachment.Cleanups.Add(AttachmentCleanupFactory.ForSourceFile(source.FileProvider));
            }
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

    private sealed record PendingWork(CancellationTokenSource CancellationTokenSource, Task Task);
}
