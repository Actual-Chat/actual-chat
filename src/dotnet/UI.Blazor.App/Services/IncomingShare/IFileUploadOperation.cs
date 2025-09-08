namespace ActualChat.UI.Blazor.App.Services;

public interface IFileUploadOperation
{
    Progress<double> Progress { get; }
    bool HasStarted { get; }
    Task<MediaContent> Task { get; }
    void Start();
    void Cancel();
}
