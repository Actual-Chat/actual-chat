using ActualChat.Concurrency;
using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Generators;
using ActualLab.IO;
using Foundation;
using PhotosUI;
using UniformTypeIdentifiers;
using Size = ActualChat.UI.App.Size;

namespace ActualChat.App.Maui;

/// <summary>
/// Loads files from PHPickerResult in background while allowing the picker to return immediately.
/// </summary>
public sealed class IosPhotoGalleryFiles : IDisposable
{
    private static readonly FilePath AttachmentsDirectory = Path.Combine(FileSystem.CacheDirectory, "attachments");
    private static readonly FilePath ThumbnailDir = new FilePath(FileSystem.CacheDirectory) | "thumbnails";

    private readonly IServiceProvider _services;
    private readonly ConcurrentProcessor<PendingFile, FilePath> _processor;

    private ILogger Log { get; }

    public IosPhotoGalleryFiles(IServiceProvider services)
    {
        _services = services;
        Log = services.LogFor(GetType());
        _processor = new ConcurrentProcessor<PendingFile, FilePath>(
            concurrencyLevel: 3,
            processor: ProcessFile,
            log: services.LogFor<ConcurrentProcessor<PendingFile, FilePath>>());
    }

    public void Dispose()
        => _processor.DisposeSilently();

    public MauiFileProvider Enqueue(PHPickerResult pickerResult)
    {
        var item = pickerResult.ItemProvider;
        FilePath fileName = item.SuggestedName.NullIfEmpty() ?? RandomStringGenerator.Default.Next(10);
        var fileType = item.ImplyMimeType();
        var ext = MediaMimeTypes.TryGetExtension(fileType, out var ext1)
            ? ext1
            : throw StandardError.Internal($"Failed to identify ext for asset '{pickerResult.AssetIdentifier}', file '{fileName}'");
        fileName = fileName.EnsureExt(ext);
        var cachedPath = AttachmentsDirectory | fileName.ToUnique();

        var pendingFile = new PendingFile(cachedPath, item);
        _processor.Enqueue(pendingFile);

        Log.LogDebug("Enqueued file '{TargetPath}' for background loading", cachedPath);

        var fileProvider = new MauiFileProvider {
            FileRef = cachedPath,
            Metadata = new() {
                FileName = fileName,
                FileType = fileType,
            },
        };
        fileProvider.Initialize(_services);
        return fileProvider;
    }

    public Task WhenNoPending(FilePath targetPath)
        => _processor.Get(new PendingFile(targetPath))?.ResultTask ?? Task.CompletedTask;

    public async Task<FilePreview?> GetPreview(FilePath targetPath, CancellationToken cancellationToken)
    {
        var pendingItem = _processor.Get(new PendingFile(targetPath));
        if (pendingItem == null)
            return null;

        return await GetThumbnail(pendingItem.Key, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FilePreview?> GetThumbnail(PendingFile pendingFile, CancellationToken cancellationToken)
    {
        // Try low-res first (faster), then standard
        string[] thumbnailUTTypeIds = [
            "com.apple.private.photos.thumbnail.low",
            "com.apple.private.photos.thumbnail.standard",
        ];

        var itemProvider = pendingFile.ItemProvider;
        foreach (var thumbnailUTTypeId in thumbnailUTTypeIds) {
            if (!itemProvider.HasItemConformingTo(thumbnailUTTypeId))
                continue;

            try {
                var representation = await itemProvider
                    .LoadInPlaceFileRepresentationAsync(thumbnailUTTypeId)
                    .ConfigureAwait(false);

                if (representation.Path.IsEmpty)
                    continue;

                // Copy thumbnail to cache directory
                var thumbnailFileName = pendingFile.TargetPath.FileName.ChangeExtension(".jpg");
                Directory.CreateDirectory(ThumbnailDir);
                var thumbnailPath = ThumbnailDir | thumbnailFileName;

                await representation.Path.CopyTo(thumbnailPath, cancellationToken).ConfigureAwait(false);

                var size = GetImageSize(thumbnailPath);
                return new FilePreview(ContentResolver.GetFileUri(thumbnailPath), size);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to load thumbnail of type '{ThumbnailType}'", thumbnailUTTypeId);
            }
        }

        return null;
    }

    private static Size? GetImageSize(FilePath path)
    {
        using var source = ImageIO.CGImageSource.FromUrl(NSUrl.CreateFileUrl(path));
        if (source == null)
            return null;

        using var properties = source.CopyProperties((NSDictionary?)null, 0);
        if (properties == null)
            return null;

        return properties[ImageIO.CGImageProperties.PixelWidth] is NSNumber width
            && properties[ImageIO.CGImageProperties.PixelHeight] is NSNumber height
                ? new Size(width.Int32Value, height.Int32Value)
                : null;
    }

    private async Task<FilePath> ProcessFile(PendingFile pendingFile, CancellationToken cancellationToken)
    {
        var targetPath = pendingFile.TargetPath;
        var item = pendingFile.ItemProvider;
        var contentType = item.RegisteredContentTypes[0];

        var loadStartedAt = CpuTimestamp.Now;
        var representation = await item
            .LoadInPlaceFileRepresentationAsync(contentType.Identifier)
            .ConfigureAwait(false);
        var sourcePath = representation.Path;

        var copyStartedAt = CpuTimestamp.Now;
        await sourcePath.CopyTo(targetPath, cancellationToken).ConfigureAwait(false);

        Log.LogInformation(
            "Loaded '{FileName}' ({Size} bytes) in {LoadElapsed} + {CopyElapsed}",
            targetPath.FileName, targetPath.FileSize,
            loadStartedAt.Elapsed.ToShortString(), copyStartedAt.Elapsed.ToShortString());

        return targetPath;
    }

    // Nested types

    private sealed record PendingFile(
        FilePath TargetPath,
        NSItemProvider ItemProvider = null!)
    {
        public bool Equals(PendingFile? other)
            => other is not null && TargetPath == other.TargetPath;

        public override int GetHashCode()
            => TargetPath.GetHashCode();
    }
}
