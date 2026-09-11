using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui;

public class WindowsFileProviderImpl(FilePath filePath) : IMauiFileProviderImpl
{
    private FileInfo FileInfo => field ??= new FileInfo(filePath);

    public Task WhenFileStreamReady()
        => Task.CompletedTask;

    public Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
        => Task.FromResult(new FilePreview(ContentResolver.GetFileUri(filePath)));

    public Task PrepareForSaving()
        => Task.CompletedTask;

    public Task ClearBeforeRemoving()
    {
        // A picked file is the user's own; only files this app wrote may be deleted
        if (MauiProcessedImageStore.Contains(filePath))
            filePath.DeleteSilently();
        return Task.CompletedTask;
    }

    public Task<Stream?> OpenRead()
        => Task.FromResult<Stream?>(FileInfo.Exists ? FileInfo.OpenRead() : null);

    public Task<string> GetContentUrl(int? decodeMaxSize, CancellationToken cancellationToken)
        => Task.FromResult(ContentResolver.GetFileUri(filePath));
}
