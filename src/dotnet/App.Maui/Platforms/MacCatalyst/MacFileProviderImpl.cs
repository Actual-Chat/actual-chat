using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui;

public class MacFileProviderImpl(FilePath filePath) : IMauiFileProviderImpl
{
    private FileInfo FileInfo => field ??= new FileInfo(filePath);

    public Task WhenFileStreamReady()
        => Task.CompletedTask;

    public Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
        => Task.FromResult(new FilePreview(ContentResolver.GetFileUri(filePath)));

    public Task PrepareForSaving()
        => Task.CompletedTask;

    public Task ClearBeforeRemoving()
        => Task.CompletedTask;

    public Task<Stream?> OpenRead()
        => Task.FromResult<Stream?>(FileInfo.Exists ? FileInfo.OpenRead() : null);
}
