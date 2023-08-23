namespace ActualChat.Chat.UI.Blazor.Services;

public interface IIncomingShareFileDownloader
{
    (Stream?, string?) OpenInputStream(string url);
    bool TryExtractFileName(string url, out string fileName);
}
