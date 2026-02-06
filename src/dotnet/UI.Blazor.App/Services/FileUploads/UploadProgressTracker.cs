namespace ActualChat.UI.Blazor.App.Services;

public class UploadProgressTracker : IProgress<double>
{
    private readonly TaskCompletionSource _tcs = TaskCompletionSourceExt.New();
    private readonly Progress<double> _progress = new ();
    private double _progressValue;

    public Task Task => _tcs.Task;

    public double Progress => _progressValue;

    public event EventHandler<double>? ProgressChanged {
        add => _progress.ProgressChanged += value;
        remove => _progress.ProgressChanged -= value;
    }

    public void SetResult()
        => _tcs.TrySetResult();

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
