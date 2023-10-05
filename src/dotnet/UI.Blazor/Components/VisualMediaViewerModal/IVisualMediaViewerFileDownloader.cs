namespace ActualChat.UI.Blazor.Components;

public interface IVisualMediaViewerFileDownloader
{
    Task Download(string uri);
}
