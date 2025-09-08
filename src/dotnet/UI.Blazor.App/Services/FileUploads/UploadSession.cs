using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

public enum UploadStatus
{
    Pending,
    Uploading,
    // Paused,
    Completed,
    Failed,
    Cancelled
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class UploadSession
{
    [DataMember, MemoryPackOrder(0)] public string SessionId { get; set; } = "";
    [DataMember, MemoryPackOrder(1)] public string FileId { get; set; } = "";
    [DataMember, MemoryPackOrder(2)] public IFileProvider FileProvider { get; set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore] public string FileName => FileProvider.FileName;

    [IgnoreDataMember, MemoryPackIgnore]
    public UploadSessionProgressTracker ProgressTracker { get; } = new ();
    // public int ChunkSize { get; set; }
    // public int TotalChunks { get; set; }
    // public int UploadedChunks { get; set; }
    [DataMember, MemoryPackOrder(3)] public UploadStatus Status { get; set; } = UploadStatus.Pending;
    [DataMember, MemoryPackOrder(4)] public Moment CreatedAt { get; set; } = Moment.Now;
    [DataMember, MemoryPackOrder(5)] public Moment LastUpdatedAt { get; set; } = Moment.Now;

    [DataMember, MemoryPackOrder(10)] public ChatId ChatId { get; set; } = null!;
}

public class UploadSessionProgressTracker
{
    private readonly TaskCompletionSource<MediaContent> _tcs = TaskCompletionSourceExt.New<MediaContent>();
    public readonly Progress<double> Progress = new ();
    public Task<MediaContent> Task => _tcs.Task;

    public void SetResult(MediaContent result)
        => _tcs.TrySetResult(result);

    public void SetCanceled()
        => _tcs.TrySetCanceled();

    public void SetException(Exception ex)
        => _tcs.TrySetException(ex);

    public void ReportProgress(double progress)
        => ((IProgress<double>)Progress).Report(progress);
}
