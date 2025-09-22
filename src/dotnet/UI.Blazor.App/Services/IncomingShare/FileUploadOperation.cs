namespace ActualChat.UI.Blazor.App.Services;

public sealed class FileUploadOperation : IFileUploadOperation, IDisposable
{
    private readonly Func<CancellationToken, Task<MediaContent>> _startFunc;
    private readonly CancellationTokenSource _cts;
    private readonly TaskCompletionSource<MediaContent> _tcs = new ();
    private readonly Progress<double> _progress;
    private long _state;

    public Task<MediaContent> Task => _tcs.Task;
    public bool HasStarted => Interlocked.Read(ref _state) != 0;
    public CancellationToken CancellationToken { get; }
    Task<MediaContent> IFileUploadOperation.Task => Task;

    public event EventHandler<double>? ProgressChanged {
        add => _progress.ProgressChanged += value;
        remove => _progress.ProgressChanged -= value;
    }

    public FileUploadOperation(Func<CancellationToken, Task<MediaContent>> startFunc, Progress<double> progress)
    {
        _startFunc = startFunc ?? throw new ArgumentNullException(nameof(startFunc));
        _progress = progress;
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
                        _tcs.TrySetCanceled();
                    else if (t.IsFaulted)
                        _tcs.TrySetException(t.Exception!.InnerExceptions);
                    else
                        _tcs.TrySetResult(t.Result);
                },
                TaskScheduler.Default);
    }

    public void Cancel()
    {
        _cts.Cancel();
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            // If not started yet, we should cancel it explicitly.
            _tcs.TrySetCanceled();
    }

    public void Dispose()
    {
        _cts.CancelAndDisposeSilently();
        _tcs.TrySetCanceled();
    }
}
