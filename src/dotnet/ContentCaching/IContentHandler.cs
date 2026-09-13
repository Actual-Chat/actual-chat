namespace ActualChat.ContentCaching;

/// <summary>
/// Handles a content request, or returns null to leave it to the native handler.
/// The caller owns the returned response and its content stream.
/// </summary>
public interface IContentHandler
{
    ValueTask<HttpResponseMessage?> Handle(ContentRequest request, CancellationToken cancellationToken = default);
}
