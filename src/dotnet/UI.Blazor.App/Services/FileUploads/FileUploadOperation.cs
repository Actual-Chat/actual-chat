using ActualChat.Media;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class FileUploadOperation : IDisposable
{
    private readonly Func<CancellationToken, Task<(UploadId, MediaId)>> _startFunc;
    private readonly CancellationTokenSource _cts;
    private long _state;

    public Task WhenReadyToStart { get; }
    public FileUploadProgressTracker ProgressTracker { get; }
    public bool HasStarted => Interlocked.Read(ref _state) != 0;
    public CancellationToken CancellationToken { get; }

    public FileUploadOperation(Task whenFileStreamReady, Func<CancellationToken, Task<(UploadId, MediaId)>> startFunc, FileUploadProgressTracker progressTracker)
    {
        _startFunc = startFunc;
        _cts = new ();
        ProgressTracker = progressTracker;
        WhenReadyToStart = whenFileStreamReady;
        CancellationToken = _cts.Token;
    }

    public void Start()
    {
        if (!WhenReadyToStart.IsCompletedSuccessfully)
            throw new InvalidOperationException("File stream is not ready yet.");

        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("Already started or cancelled.");

        _ = _startFunc(CancellationToken)
            .ContinueWith(t => {
                    if (t.IsCanceled)
                        ProgressTracker.SetCanceled();
                    else if (t.IsFaulted)
                        ProgressTracker.SetException(t.Exception);
                    else
                        ProgressTracker.SetResult(t.GetAwaiter().GetResult());
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
        => _cts.DisposeSilently();
}

// Progress tracker for file upload operation (returns UploadId + MediaId)
public class FileUploadProgressTracker : IProgress<double>
{
    private readonly TaskCompletionSource<(UploadId, MediaId)> _tcs = TaskCompletionSourceExt.New<(UploadId, MediaId)>();
    private readonly Progress<double> _progress = new ();
    private double _progressValue;

    public Task<(UploadId, MediaId)> Task => _tcs.Task;
    public double Progress => _progressValue;

    public event EventHandler<double>? ProgressChanged {
        add => _progress.ProgressChanged += value;
        remove => _progress.ProgressChanged -= value;
    }

    public void SetResult((UploadId, MediaId) result)
        => _tcs.TrySetResult(result);

    public void SetCanceled()
        => _tcs.TrySetCanceled();

    public void SetException(Exception ex)
        => _tcs.TrySetException(ex);

    public void ReportProgress(double progress)
    {
        Interlocked.Exchange(ref _progressValue, progress);
        ((IProgress<double>)_progress).Report(progress);
    }

    void IProgress<double>.Report(double value)
        => ReportProgress(value);
}
