using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.IO;

namespace ActualChat.App.Maui;

public sealed class IosFileProviderImpl(IServiceProvider services, FilePath filePath) : IMauiFileProviderImpl
{
    private IosVideoThumbnails VideoThumbnails => field ??= services.GetRequiredService<IosVideoThumbnails>();
    private IosPhotoGalleryFiles PhotoGalleryFiles => field ??= services.GetRequiredService<IosPhotoGalleryFiles>();
    private ILogger? DebugLog => field ??= services.LogFor(GetType()).IfEnabled(LogLevel.Debug, Constants.DebugMode.FileAttachments);

    public Task WhenFileStreamReady()
        => PhotoGalleryFiles.WhenNoPending(filePath);

    public async Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
    {
        await WhenFileStreamReady().ConfigureAwait(false);

        if (!OrdinalIgnoreCaseEquals(filePath.Extension, ".mov"))
            return new FilePreview(ContentResolver.GetFileUri(filePath));

        var thumbnail = await VideoThumbnails.Generate(filePath, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogDebug("Generated thumbnail: {ThumbnailPath}", thumbnail?.Path);
        return thumbnail is { } t
            ? new FilePreview(ContentResolver.GetFileUri(t.Path), t.Size)
            : new FilePreview(ContentResolver.GetFileUri(filePath));
    }

    public Task PrepareForSaving()
        => Task.CompletedTask;

    public Task ClearBeforeRemoving()
    {
        File.Delete(filePath);
        return Task.CompletedTask;
    }

    public async Task<Stream?> OpenRead()
    {
        await WhenFileStreamReady().ConfigureAwait(false);
        return File.OpenRead(filePath);
    }
}
