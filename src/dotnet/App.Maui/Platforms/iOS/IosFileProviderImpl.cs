using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui;

public sealed class IosFileProviderImpl(FilePath filePath) : IMauiFileProviderImpl
{
    private FileInfo FileInfo => field ??= new FileInfo(filePath);

    public Task<string> GetPreviewUrl()
        => Task.FromResult(ContentResolver.GetFileUri(filePath));

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
