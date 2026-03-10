using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui;

public sealed class IosFileProviderImpl(IServiceProvider services, FilePath filePath) : IMauiFileProviderImpl
{
    private IosPhotoGalleryFiles PhotoGalleryFiles => field ??= services.GetRequiredService<IosPhotoGalleryFiles>();

    public Task WhenFileStreamReady()
        => PhotoGalleryFiles.WhenFileReady(filePath);

    public async Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
    {
        // Try to get preview from photo gallery (awaits if pending)
        var preview = await PhotoGalleryFiles.GetPreview(filePath).ConfigureAwait(false);
        if (preview is not null)
            return preview;

        return File.Exists(filePath)
            ? new FilePreview(ContentResolver.GetFileUri(filePath))
            : throw StandardError.Internal($"Unable to generate file preview for '{filePath}'.");
    }

    public Task PrepareForSaving()
        => Task.CompletedTask;

    public Task ClearBeforeRemoving()
    {
        filePath.DeleteSilently();
        return Task.CompletedTask;
    }

    public async Task<Stream?> OpenRead()
    {
        await WhenFileStreamReady().ConfigureAwait(false);
        return File.OpenRead(filePath);
    }
}
