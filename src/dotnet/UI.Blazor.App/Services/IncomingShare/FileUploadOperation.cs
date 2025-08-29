namespace ActualChat.UI.Blazor.App.Services;

public sealed class FileUploadOperation<TResult> : IDisposable
{
    private readonly Func<CancellationToken, Task<TResult>> _startFunc;
    private readonly CancellationTokenSource _cts;
    private readonly TaskCompletionSource<TResult> _tcs = new ();
    private int _state;

    public Progress<double>? Progress { get; init; }
    public Task<TResult> Task => _tcs.Task;
    public CancellationToken CancellationToken { get; }

    public FileUploadOperation(Func<CancellationToken, Task<TResult>> startFunc)
    {
        _startFunc = startFunc ?? throw new ArgumentNullException(nameof(startFunc));
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
