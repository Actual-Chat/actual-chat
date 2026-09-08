using ActualChat.UI.Blazor.App.Services;
using ActualLab.Generators;
using ActualLab.IO;
using AVFoundation;
using CoreGraphics;
using Foundation;
using ImageIO;
using PhotosUI;
using UniformTypeIdentifiers;

namespace ActualChat.App.Maui;

/// <summary>
/// Loads files from PHPickerResult in background while allowing the picker to return immediately.
/// Provides two-phase processing: preview (thumbnail) first, then main file; when the picker
/// offers no thumbnail (macOS), one is generated from the loaded file.
/// </summary>
public sealed class ApplePhotoGalleryFiles(IServiceProvider services) : ProcessorBase
{
    private const int ThumbnailMaxPixelSize = 1024;
    private const float ThumbnailJpegQuality = 0.8f;
    private static readonly TimeSpan VideoThumbnailTime = TimeSpan.FromSeconds(0.5);
    private static readonly CGImageThumbnailOptions ThumbnailOptions = new() {
        CreateThumbnailFromImageAlways = true,
        CreateThumbnailWithTransform = true,
        MaxPixelSize = ThumbnailMaxPixelSize,
    };
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
        var thumbnailPath = GetThumbnailPath(videoPath);
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
            // Phase 1: the picker's own thumbnail (iOS provides one, macOS doesn't)
            var preview = await CreatePreview(item.TargetPath, item.ItemProvider, cancellationToken).ConfigureAwait(false);
            if (preview is not null)
                item.SetPreview(preview);

            // Phase 2: Load main file
            await LoadMainFile(item, cancellationToken).ConfigureAwait(false);
            if (preview is null)
                item.SetPreview(await CreatePreviewFromFile(item, cancellationToken).ConfigureAwait(false));
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
                Directory.CreateDirectory(ThumbnailDir);
                var thumbnailPath = GetThumbnailPath(targetPath);
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

    private async Task<FilePreview?> CreatePreviewFromFile(PendingItem item, CancellationToken cancellationToken)
    {
        var targetPath = item.TargetPath;
        try {
            var isVideo = item.ContentType.ConformsTo(UTTypes.Movie);
            var (image, durationMs) = isVideo
                ? await CreateVideoThumbnail(targetPath, cancellationToken).ConfigureAwait(false)
                : (CreateImageThumbnail(targetPath), 0L);
            if (image is null) {
                Log.LogDebug("No preview generated for '{TargetPath}'", targetPath);
                return null;
            }

            FilePreview preview;
            using (image)
                preview = SaveThumbnail(image, targetPath, durationMs);
            Log.LogDebug("Generated preview for '{TargetPath}': {Url}", targetPath, preview.Url);
            return preview;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to generate preview for '{TargetPath}'", targetPath);
            return null;
        }
    }

    private static CGImage? CreateImageThumbnail(FilePath path)
    {
        using var source = CGImageSource.FromUrl(NSUrl.CreateFileUrl(path));
        return source?.CreateThumbnail(0, ThumbnailOptions);
    }

    private static async Task<(CGImage? Image, long DurationMs)> CreateVideoThumbnail(
        FilePath path, CancellationToken cancellationToken)
    {
        using var asset = AVAsset.FromUrl(NSUrl.CreateFileUrl(path));
        await asset.LoadValuesTaskAsync(["duration"]).ConfigureAwait(false);
        var duration = TimeSpan.FromSeconds(asset.Duration.Seconds);
        using var generator = new AVAssetImageGenerator(asset);
        generator.AppliesPreferredTrackTransform = true;
        generator.MaximumSize = new CGSize(ThumbnailMaxPixelSize, ThumbnailMaxPixelSize);
        var time = duration < VideoThumbnailTime * 2 ? duration / 2 : VideoThumbnailTime;
        var image = await generator.GenerateCGImage(time, cancellationToken).ConfigureAwait(false);
        return (image, (long)duration.TotalMilliseconds);
    }

    private static FilePreview SaveThumbnail(CGImage image, FilePath targetPath, long durationMs)
    {
        Directory.CreateDirectory(ThumbnailDir);
        var thumbnailPath = GetThumbnailPath(targetPath);
        SaveJpeg(image, thumbnailPath);
        var size = new Size2D((int)image.Width, (int)image.Height);
        return new FilePreview(ContentResolver.GetFileUri(thumbnailPath), size, durationMs);
    }

    private static void SaveJpeg(CGImage image, FilePath path)
    {
        using var destination = CGImageDestination.Create(NSUrl.CreateFileUrl(path), UTTypes.Jpeg.Identifier, 1)
            ?? throw StandardError.Internal($"Unable to create image destination '{path}'.");
        destination.AddImage(image, new CGImageDestinationOptions { LossyCompressionQuality = ThumbnailJpegQuality });
        if (!destination.Close())
            throw StandardError.Internal($"Unable to write thumbnail '{path}'.");
    }

    private static FilePath GetThumbnailPath(FilePath targetPath)
        => ThumbnailDir | targetPath.FileName.ChangeExtension(".jpg");

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
