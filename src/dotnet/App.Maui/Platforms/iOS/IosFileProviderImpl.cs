using ActualChat.Maui;
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

    public async Task<string> GetPreviewUrl()
    {
        if (!OrdinalIgnoreCaseEquals(filePath.Extension, ".mov"))
            return ContentResolver.GetFileUri(filePath);

        var thumbnailPath = await VideoThumbnails.Generate(filePath).ConfigureAwait(false);
        DebugLog?.LogDebug("Generated thumbnail: {ThumbnailPath}", thumbnailPath);
        return ContentResolver.GetFileUri(thumbnailPath.IsEmpty ? filePath : thumbnailPath);
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
