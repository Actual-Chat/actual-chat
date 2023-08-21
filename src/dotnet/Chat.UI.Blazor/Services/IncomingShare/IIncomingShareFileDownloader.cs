namespace ActualChat.Chat.UI.Blazor.Services;

public interface IIncomingShareFileDownloader
{
    (Stream?, string?) OpenInputStream(string url);
}
