namespace ActualChat.UI.Blazor.App.Services;

public interface IFileUploaderBackend
{
    void OnUploadProgress(int progress);
    void OnUploadSucceed(MediaId mediaId, MediaId thumbnailMediaId);
    void OnUploadFailed();
}

public sealed class FileUploaderBackend : IFileUploaderBackend, IDisposable
{
    private bool _isDisposed;

    public FileUploaderTracker Tracker { get; } = new ();
    public DotNetObjectReference<IFileUploaderBackend> BlazorRef { get; }

    public FileUploaderBackend()
        => BlazorRef = DotNetObjectReference.Create<IFileUploaderBackend>(this);

    [JSInvokable]
    public void OnUploadProgress(int progress)
        => Tracker.ReportProgress(progress);

    [JSInvokable]
    public void OnUploadSucceed(MediaId mediaId, MediaId thumbnailMediaId)
        => Tracker.SetResult(new MediaContent(mediaId, "", thumbnailMediaId, ""));

    [JSInvokable]
    public void OnUploadFailed()
        => Tracker.SetException(StandardError.Internal("Upload failed."));

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        BlazorRef.DisposeSilently();
    }
}
