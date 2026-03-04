using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.IO;

namespace ActualChat.App.Maui;

public sealed class IosFileProviderImpl(IServiceProvider services, FilePath filePath) : IMauiFileProviderImpl
{
    private IosVideoThumbnails VideoThumbnails => field ??= services.GetRequiredService<IosVideoThumbnails>();
    private ILogger Log => field ??= services.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.FileAttachments);

    private FileInfo FileInfo => field ??= new FileInfo(filePath);

    public async Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
    {
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

    public Task<Stream?> OpenRead()
        => Task.FromResult<Stream?>(FileInfo.Exists ? FileInfo.OpenRead() : null);
}
