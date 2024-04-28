namespace ActualChat.Media;

public interface ICrawlingHandler
{
    bool Supports(HttpResponseMessage response);
    Task<CrawledLink> Handle(HttpResponseMessage response, CancellationToken cancellationToken);
}
