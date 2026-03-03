using ActualChat.Maui;
using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui;

public sealed class IosFileProviderImpl(IosVideoThumbnails videoThumbnails, FilePath filePath) : IMauiFileProviderImpl
{
    private FileInfo FileInfo => field ??= new FileInfo(filePath);

    public async Task<string> GetPreviewUrl()
    {
        if (!OrdinalIgnoreCaseEquals(filePath.Extension, ".mov"))
            return ContentResolver.GetFileUri(filePath);

        var thumbnailPath = await videoThumbnails.Generate(filePath).ConfigureAwait(false);
        return thumbnailPath.IsEmpty
            ? ContentResolver.GetFileUri(filePath)
            : ContentResolver.GetFileUri(thumbnailPath);
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
