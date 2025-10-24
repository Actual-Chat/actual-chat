namespace ActualChat.UI.Blazor.App.Services;

public interface IFileUploadOperation
{
    UploadProgressTracker ProgressTracker { get; }
    bool HasStarted { get; }
    void Start();
    void Cancel();
}
