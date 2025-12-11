namespace ActualChat.UI.Blazor.App.Services;

public interface IWebFileUploaderBackend
{
    void OnUploadProgress(int progress);
    void OnUploadSucceed();
    void OnUploadFailed();
    void OnUploadCancelled();
}

public sealed class WebFileUploaderBackend : IWebFileUploaderBackend, IDisposable
{
    private bool _isDisposed;
    private readonly TaskCompletionSource _tcs = TaskCompletionSourceExt.New();

    public IProgress<double> ProgressTracker { get; }
    public Task WhenUploadCompleted => _tcs.Task;
    public DotNetObjectReference<IWebFileUploaderBackend> BlazorRef { get; }

    public WebFileUploaderBackend(IProgress<double> progressTracker)
    {
        ProgressTracker = progressTracker;
        BlazorRef = DotNetObjectReference.Create<IWebFileUploaderBackend>(this);
    }

    [JSInvokable]
    public void OnUploadProgress(int progress)
        => ProgressTracker.Report(progress);

    [JSInvokable]
    public void OnUploadSucceed()
        => _tcs.TrySetResult();

    [JSInvokable]
    public void OnUploadNotFound()
        => _tcs.TrySetException(StandardError.Upload.NotFound());

    [JSInvokable]
    public void OnUploadFailed()
        => _tcs.TrySetException(StandardError.Internal("Upload failed."));

    [JSInvokable]
    public void OnUploadCancelled()
        => _tcs.TrySetCanceled();

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        BlazorRef.DisposeSilently();
    }
}
