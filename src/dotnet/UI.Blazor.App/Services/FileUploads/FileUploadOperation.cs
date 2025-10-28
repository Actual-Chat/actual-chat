namespace ActualChat.UI.Blazor.App.Services;

public sealed class FileUploadOperation : IFileUploadOperation, IDisposable
{
    private readonly Func<CancellationToken, Task<MediaContent>> _startFunc;
    private readonly CancellationTokenSource _cts;
    private long _state;

    public UploadProgressTracker ProgressTracker { get; }
    public bool HasStarted => Interlocked.Read(ref _state) != 0;
    public CancellationToken CancellationToken { get; }

    public FileUploadOperation(Func<CancellationToken, Task<MediaContent>> startFunc, UploadProgressTracker progressTracker)
    {
        _startFunc = startFunc;
        ProgressTracker = progressTracker;
        _cts = new ();
        CancellationToken = _cts.Token;
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("Already started or cancelled.");

        _ = _startFunc(CancellationToken)
            .ContinueWith(t => {
                    if (t.IsCanceled)
                        ProgressTracker.SetCanceled();
                    else if (t.IsFaulted)
                        ProgressTracker.SetException(t.Exception);
                    else
                        ProgressTracker.SetResult(t.Result);
                },
                TaskScheduler.Default);
    }

    public void Cancel()
    {
        _cts.Cancel();
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            // If not started yet, we should cancel it explicitly.
            ProgressTracker.SetCanceled();
    }

    public void Dispose()
    {
        _cts.CancelAndDisposeSilently();
        ProgressTracker.SetCanceled();
    }
}
