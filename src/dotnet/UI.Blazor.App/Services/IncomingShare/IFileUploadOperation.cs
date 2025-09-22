namespace ActualChat.UI.Blazor.App.Services;

public interface IFileUploadOperation
{
    event EventHandler<double> ProgressChanged;
    bool HasStarted { get; }
    Task<MediaContent> Task { get; }
    void Start();
    void Cancel();
}
