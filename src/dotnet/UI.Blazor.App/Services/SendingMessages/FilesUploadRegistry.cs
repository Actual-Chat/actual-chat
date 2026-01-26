namespace ActualChat.UI.Blazor.App.Services;

public class FilesUploadRegistry
{
    private readonly Dictionary<FilesUploadHandle, FilesUpload> _uploads = new();

    public FilesUploadHandle Register(FilesUpload upload)
    {
        lock (_uploads) {
            var handle = new FilesUploadHandle(h => {
                Unregister(h);
                upload.ReviewUsages();
            });
            _uploads.Add(handle, upload);
            return handle;
        }
    }

    public FilesUpload Get(FilesUploadHandle handle)
    {
        lock (_uploads)
            return _uploads.TryGetValue(handle, out var upload) ? upload : throw new InvalidOperationException("No upload registered for the handle");
    }

    private void Unregister(FilesUploadHandle handle)
    {
        lock (_uploads)
            _uploads.Remove(handle);
    }
}

public class FilesUploadHandle(Action<FilesUploadHandle> onDisposed) : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        onDisposed(this);
    }
}
