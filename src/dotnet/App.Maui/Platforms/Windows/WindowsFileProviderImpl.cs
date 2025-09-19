using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public class WindowsFileProviderImpl(string filePath) : IMauiFileProviderImpl
{
    [field: AllowNull, MaybeNull]
    private FileInfo FileInfo => field ??= new FileInfo(filePath);

    public Task<string> GetPreviewUrl()
        => Task.FromResult(ContentResolver.GetFileUri(filePath));

    public Task PrepareForSaving()
        => Task.CompletedTask;

    public Task ClearBeforeRemoving()
        => Task.CompletedTask;

    public Task<Stream?> OpenRead()
        => Task.FromResult<Stream?>(FileInfo.Exists ? FileInfo.OpenRead() : null);
}
