namespace ActualChat.UI.Blazor.App.Services;

public interface IIncomingShareFileDownloader
{
    (Stream?, string?) OpenInputStream(string url);
    bool TryExtractFileName(string url, out string fileName);
}
