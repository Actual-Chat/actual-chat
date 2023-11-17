namespace ActualChat.UI.Blazor.Components;

public interface IVisualMediaViewerFileDownloader
{
    Task Download(string uri, string contentType);
    bool IsInProgress(string downloadUrl);
    Task Cancel(string downloadUrl);
}
