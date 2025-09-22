using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

public enum UploadStatus
{
    Pending,
    Uploading,
    Completed,
    Failed,
    Canceled
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class UploadSession
{
    [DataMember, MemoryPackOrder(0)] public string SessionId { get; set; } = "";
    [DataMember, MemoryPackOrder(1)] public string FileId { get; set; } = "";
    [DataMember, MemoryPackOrder(2)] public IFileProvider FileProvider { get; set; } = null!;
    [DataMember, MemoryPackOrder(3)] public UploadStatus Status { get; set; } = UploadStatus.Pending;
    [DataMember, MemoryPackOrder(4)] public Moment CreatedAt { get; set; } = Moment.EpochStart;
    [DataMember, MemoryPackOrder(5)] public Moment LastUpdatedAt { get; set; } = Moment.EpochStart;

    [DataMember, MemoryPackOrder(10)] public ChatId ChatId { get; set; } = null!;

    [IgnoreDataMember, MemoryPackIgnore] public string FileName => FileProvider.FileName;
    [IgnoreDataMember, MemoryPackIgnore] public UploadSessionProgressTracker ProgressTracker { get; } = new ();
}

public class UploadSessionProgressTracker
{
    private readonly TaskCompletionSource<MediaContent> _tcs = TaskCompletionSourceExt.New<MediaContent>();
    private readonly Progress<double> _progress = new ();
    private double _progressValue;

    public Task<MediaContent> Task => _tcs.Task;

    public double Progress => _progressValue;

    public event EventHandler<double>? ProgressChanged {
        add => _progress.ProgressChanged += value;
        remove => _progress.ProgressChanged -= value;
    }

    public void SetResult(MediaContent result)
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
}
