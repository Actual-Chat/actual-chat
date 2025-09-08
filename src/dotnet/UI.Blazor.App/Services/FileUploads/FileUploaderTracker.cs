namespace ActualChat.UI.Blazor.App.Services;

public class FileUploaderTracker
{
    private readonly TaskCompletionSource<MediaContent> _tcs = TaskCompletionSourceExt.New<MediaContent>();
    public readonly Progress<double> Progress = new ();
    public Task<MediaContent> Task => _tcs.Task;

    public void SetResult(MediaContent content)
        => _tcs.TrySetResult(content);

    public void SetCanceled()
        => _tcs.TrySetCanceled();

    public void SetException(Exception ex)
        => _tcs.TrySetException(ex);

    public void ReportProgress(double progress)
        => ((IProgress<double>)Progress).Report(progress);
}
