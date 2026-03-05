using ActualChat.Concurrency;
using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;
using Foundation;
using PhotosUI;
using UniformTypeIdentifiers;

namespace ActualChat.App.Maui;

/// <summary>
/// Loads files from PHPickerResult in background while allowing the picker to return immediately.
/// </summary>
public sealed class IosPhotoGalleryFiles : IDisposable
{
    private static readonly FilePath AttachmentsDirectory = Path.Combine(FileSystem.CacheDirectory, "attachments");

    private readonly IServiceProvider _services;
    private readonly ConcurrentProcessor<PendingFile, FilePath> _processor;

    private ILogger Log { get; }
    private IosVideoThumbnails VideoThumbnails => field ??= _services.GetRequiredService<IosVideoThumbnails>();

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

    public MauiFileProvider Enqueue(PHPickerResult pickerResult, UTType preferredContentType)
    {
        var item = pickerResult.ItemProvider;
        FilePath suggestedFileName = item.SuggestedName.NullIfEmpty();
        var ext = MediaMimeTypes.TryGetExtension(item.ImplyMimeType(), out var ext1)
            ? ext1
            : throw StandardError.Internal($"Failed to identify ext for asset {pickerResult.AssetIdentifier}");
        var targetPath = AttachmentsDirectory | suggestedFileName.EnsureExt(ext).ToUnique();

        var pendingFile = new PendingFile(targetPath, item, preferredContentType);
        _processor.Enqueue(pendingFile);

        Log.LogDebug("Enqueued file '{TargetPath}' for background loading", targetPath);

        var fileProvider = new MauiFileProvider {
            FileRef = targetPath,
            Metadata = new() {
                FileName = suggestedFileName,
                FileType = item.ImplyMimeType(),
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

        var itemProvider = pendingItem.Key.ItemProvider;
        var contentType = pendingItem.Key.ContentType;

        // Try to get in-place URL for quick thumbnail generation (doesn't require full file copy)
        if (OrdinalIgnoreCaseEquals(targetPath.Extension, ".mov")) {
            var thumbnail = await GenerateThumbnailFromInPlaceUrl(itemProvider, contentType, targetPath, cancellationToken)
                .ConfigureAwait(false);
            if (thumbnail != null)
                return thumbnail;
        }

        // Wait for file to be fully loaded
        await pendingItem.ResultTask.ConfigureAwait(false);

        if (!OrdinalIgnoreCaseEquals(targetPath.Extension, ".mov"))
            return new FilePreview(ContentResolver.GetFileUri(targetPath));

        var fallbackThumbnail = await VideoThumbnails.Generate(targetPath, cancellationToken).ConfigureAwait(false);
        Log.LogDebug("Generated thumbnail: {ThumbnailPath}", fallbackThumbnail?.Path);
        return fallbackThumbnail is { } t
            ? new FilePreview(ContentResolver.GetFileUri(t.Path), t.Size)
            : new FilePreview(ContentResolver.GetFileUri(targetPath));
    }

    private async Task<FilePreview?> GenerateThumbnailFromInPlaceUrl(
        NSItemProvider itemProvider,
        UTType contentType,
        FilePath targetPath,
        CancellationToken cancellationToken)
    {
        try {
            var result = await itemProvider
                .LoadInPlaceFileRepresentationAsync(contentType.Identifier)
                .ConfigureAwait(false);

            if (result.Path.Value.IsNullOrEmpty())
                return null;

            Log.LogDebug("Got in-place path: {Path}", result.Path);

            var thumbnail = await VideoThumbnails.Generate(result.Path, cancellationToken)
                .ConfigureAwait(false);

            return thumbnail is { } t
                ? new FilePreview(ContentResolver.GetFileUri(t.Path), t.Size)
                : null;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to generate thumbnail from in-place URL");
            return null;
        }
    }

    private async Task<FilePath> ProcessFile(PendingFile pendingFile, CancellationToken cancellationToken)
    {
        var targetPath = pendingFile.TargetPath;
        var item = pendingFile.ItemProvider;
        var contentType = pendingFile.ContentType;

        var loadStartedAt = CpuTimestamp.Now;
        var representation = await item
            .LoadFileRepresentationAsync(contentType.Identifier)
            .ConfigureAwait(false);
        var sourcePath = (FilePath)representation.Path!;

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
        NSItemProvider ItemProvider = null!,
        UTType ContentType = null!)
    {
        public bool Equals(PendingFile? other)
            => other is not null && TargetPath == other.TargetPath;

        public override int GetHashCode()
            => TargetPath.GetHashCode();
    }
}
