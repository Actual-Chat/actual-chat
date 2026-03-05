using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui;

public sealed class IosFileProviderImpl(IServiceProvider services, FilePath filePath) : IMauiFileProviderImpl
{
    private IosPhotoGalleryFiles PhotoGalleryFiles => field ??= services.GetRequiredService<IosPhotoGalleryFiles>();
    private IosVideoThumbnails VideoThumbnails => field ??= services.GetRequiredService<IosVideoThumbnails>();

    public Task WhenFileStreamReady()
        => PhotoGalleryFiles.WhenNoPending(filePath);

    public async Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
    {
        var preview = await PhotoGalleryFiles.GetPreview(filePath, cancellationToken).ConfigureAwait(false);
        if (preview is not null)
            return preview;

        if (File.Exists(filePath))
            return await GetPreviewCore(cancellationToken).ConfigureAwait(false);

        throw StandardError.Internal($"Unable to generate file preview for '{filePath}'.");
    }

    private async Task<FilePreview> GetPreviewCore(CancellationToken cancellationToken)
    {
        if (!OrdinalIgnoreCaseEquals(filePath.Extension, ".mov"))
            return new FilePreview(ContentResolver.GetFileUri(filePath));

        var thumbnail = await VideoThumbnails.Generate(filePath, cancellationToken).ConfigureAwait(false);
        return thumbnail is { } t
            ? new FilePreview(ContentResolver.GetFileUri(t.Path), t.Size)
            : new FilePreview(ContentResolver.GetFileUri(filePath));
    }

    public Task PrepareForSaving()
        => Task.CompletedTask;

    public Task ClearBeforeRemoving()
    {
        try {
            File.Delete(filePath);
        }
        catch (DirectoryNotFoundException) {
            // File or directory doesn't exist - ignore, since we're deleting anyway
        }
        return Task.CompletedTask;
    }

    public async Task<Stream?> OpenRead()
    {
        await WhenFileStreamReady().ConfigureAwait(false);
        return File.OpenRead(filePath);
    }
}
