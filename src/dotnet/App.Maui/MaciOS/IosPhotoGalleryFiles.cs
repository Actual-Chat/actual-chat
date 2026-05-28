using ActualChat.UI.Blazor.App.Services;
using ActualLab.Generators;
using ActualLab.IO;
using Foundation;
using PhotosUI;
using UniformTypeIdentifiers;

namespace ActualChat.App.Maui;

/// <summary>
/// Loads files from PHPickerResult in background while allowing the picker to return immediately.
/// Provides two-phase processing: preview (thumbnail) first, then main file.
/// </summary>
public sealed class IosPhotoGalleryFiles(IServiceProvider services) : ProcessorBase
{
    private static readonly FilePath AttachmentsDir = new FilePath(FileSystem.CacheDirectory) | "attachments";
    private static readonly FilePath ThumbnailDir = new FilePath(FileSystem.CacheDirectory) | "thumbnails";
    private static readonly string[] ThumbnailUTTypeIds = [
        "com.apple.private.photos.thumbnail.low",
        "com.apple.private.photos.thumbnail.standard",
    ];

    private readonly ConcurrentDictionary<FilePath, PendingItem> _items = new();

    private ILogger Log => field ??= services.LogFor(GetType());

    public MauiFileProvider Enqueue(PHPickerResult pickerResult)
    {
        var itemProvider = pickerResult.ItemProvider;
        var contentType = itemProvider.PickMainContentType();
        var fileType = contentType.PreferredMimeType.NullIfEmpty().RequireNonEmpty();

        FilePath fileName = itemProvider.SuggestedName.NullIfEmpty() ?? RandomStringGenerator.Default.Next(10);
        fileName = fileName.EnsureExt(contentType.PreferredFilenameExtension.RequireNonEmpty());
        var targetPath = AttachmentsDir | fileName.ToUnique();

        var pendingItem = new PendingItem(targetPath, itemProvider, contentType);
        _items[targetPath] = pendingItem;

        Log.LogDebug("Enqueued file '{TargetPath}' for background loading", targetPath);

        // Start processing in background
        _ = ProcessPhotoGalleryItem(pendingItem, StopToken);

        var fileProvider = new MauiFileProvider {
            FileRef = targetPath,
            Metadata = new() {
                FileName = fileName,
                FileType = fileType,
            },
        };
        fileProvider.Initialize(services);
        return fileProvider;
    }

    public Task<FilePreview?> GetPreview(FilePath targetPath)
        => _items.TryGetValue(targetPath, out var item)
            ? item.PreviewTask
            : Task.FromResult<FilePreview?>(null);

    public Task WhenFileReady(FilePath targetPath)
        => _items.TryGetValue(targetPath, out var item)
            ? item.FileTask
            : Task.CompletedTask;

    /// <summary>
    /// Finds an existing thumbnail file for the given video path.
    /// Used after app restart when in-memory tracking is lost but cached files might still exist.
    /// </summary>
    public FilePreview? FindExistingThumbnail(FilePath videoPath)
    {
        var thumbnailFileName = videoPath.FileName.ChangeExtension(".jpg");
        var thumbnailPath = ThumbnailDir | thumbnailFileName;

        if (!File.Exists(thumbnailPath))
            return null;

        var size = GetImageSize(thumbnailPath);
        var preview = new FilePreview(ContentResolver.GetFileUri(thumbnailPath), size);
        Log.LogDebug("Found existing thumbnail for '{VideoPath}': {Url}", videoPath, preview.Url);
        return preview;
    }

    private async Task ProcessPhotoGalleryItem(PendingItem item, CancellationToken cancellationToken)
    {
        try {
            // Phase 1: Create preview (thumbnail)
            var preview = await CreatePreview(item.TargetPath, item.ItemProvider, cancellationToken).ConfigureAwait(false);
            item.SetPreview(preview);

            // Phase 2: Load main file
            await LoadMainFile(item, cancellationToken).ConfigureAwait(false);
            item.SetFileReady();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to process file '{TargetPath}'", item.TargetPath);
            item.SetFailed(e);
        }
    }

    private async Task<FilePreview?> CreatePreview(FilePath targetPath, NSItemProvider itemProvider, CancellationToken cancellationToken)
    {
        foreach (var thumbnailUTTypeId in ThumbnailUTTypeIds) {
            if (!itemProvider.HasItemConformingTo(thumbnailUTTypeId))
                continue;

            try {
                var representation = await itemProvider
                    .LoadInPlaceFileRepresentationAsync(thumbnailUTTypeId)
                    .ConfigureAwait(false);

                if (representation.Path.IsEmpty)
                    continue;

                // Copy thumbnail to cache directory
                var thumbnailFileName = targetPath.FileName.ChangeExtension(".jpg");
                Directory.CreateDirectory(ThumbnailDir);
                var thumbnailPath = ThumbnailDir | thumbnailFileName;
                await representation.Copy(thumbnailPath, cancellationToken).ConfigureAwait(false);

                var size = GetImageSize(thumbnailPath);
                var preview = new FilePreview(ContentResolver.GetFileUri(thumbnailPath), size);
                Log.LogDebug("Created preview for '{TargetPath}': {Url}", targetPath, preview.Url);
                return preview;
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to load thumbnail of type '{ThumbnailType}'", thumbnailUTTypeId);
            }
        }

        Log.LogDebug("No preview available for '{TargetPath}'", targetPath);
        return null;
    }

    private async Task LoadMainFile(PendingItem item, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AttachmentsDir);
        var targetPath = item.TargetPath;
        Log.LogDebug(
            "Loading '{FileName}' as '{ContentType}' (registered: [{RegisteredTypes}])",
            targetPath.FileName, item.ContentType.Identifier,
            string.Join(", ", item.ItemProvider.RegisteredContentTypes.Select(t => t.Identifier)));

        var loadStartedAt = CpuTimestamp.Now;
        var representation = await item.ItemProvider
            .LoadInPlaceFileRepresentationAsync(item.ContentType.Identifier)
            .ConfigureAwait(false);
        var sourcePath = representation.Path;

        var copyStartedAt = CpuTimestamp.Now;
        await sourcePath.CopyFile(targetPath, cancellationToken).ConfigureAwait(false);

        Log.LogInformation(
            "Loaded '{FileName}' ({Size} bytes) in {LoadElapsed} + {CopyElapsed}",
            targetPath.FileName, targetPath.FileSize,
            loadStartedAt.Elapsed.ToShortString(), copyStartedAt.Elapsed.ToShortString());
    }

    private static Size2D? GetImageSize(FilePath path)
    {
        using var source = ImageIO.CGImageSource.FromUrl(NSUrl.CreateFileUrl(path));
        if (source == null)
            return null;

        using var properties = source.CopyProperties((NSDictionary?)null, 0);
        if (properties == null)
            return null;

        return properties[ImageIO.CGImageProperties.PixelWidth] is NSNumber width
            && properties[ImageIO.CGImageProperties.PixelHeight] is NSNumber height
                ? new Size2D(width.Int32Value, height.Int32Value)
                : null;
    }

    // Nested types

    private sealed class PendingItem(FilePath targetPath, NSItemProvider itemProvider, UTType contentType)
    {
        private readonly TaskCompletionSource<FilePreview?> _previewTcs = TaskCompletionSourceExt.New<FilePreview?>();
        private readonly TaskCompletionSource _fileTcs = TaskCompletionSourceExt.New();

        public FilePath TargetPath => targetPath;
        public NSItemProvider ItemProvider => itemProvider;
        public UTType ContentType => contentType;

        public Task<FilePreview?> PreviewTask => _previewTcs.Task;
        public Task FileTask => _fileTcs.Task;

        public void SetPreview(FilePreview? preview)
            => _previewTcs.TrySetResult(preview);

        public void SetFileReady()
            => _fileTcs.TrySetResult();

        public void SetFailed(Exception e)
        {
            _previewTcs.TrySetException(e);
            _fileTcs.TrySetException(e);
        }
    }
}
