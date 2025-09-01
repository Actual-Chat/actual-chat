namespace ActualChat.UI.Blazor.App.Services;

public interface IFileUploadOperation
{
    bool HasStarted { get; }
    Task Task { get; }
    void Start();
    void Cancel();
}
