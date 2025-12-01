namespace ActualChat.UI.Blazor.App.Services;

public interface IWebFileUploaderBackend
{
    void OnUploadProgress(int progress);
    void OnUploadSucceed();
    void OnUploadFailed();
}

public sealed class WebFileUploaderBackend : IWebFileUploaderBackend, IDisposable
{
    private bool _isDisposed;
    private readonly TaskCompletionSource _tcs = TaskCompletionSourceExt.New();

    public UploadProgressTracker Tracker { get; } = new ();
    public Task WhenUploadCompleted => _tcs.Task;
    public DotNetObjectReference<IWebFileUploaderBackend> BlazorRef { get; }

    public WebFileUploaderBackend()
        => BlazorRef = DotNetObjectReference.Create<IWebFileUploaderBackend>(this);

    [JSInvokable]
    public void OnUploadProgress(int progress)
        => Tracker.ReportProgress(progress);

    [JSInvokable]
    public void OnUploadSucceed()
        => _tcs.TrySetResult();

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
